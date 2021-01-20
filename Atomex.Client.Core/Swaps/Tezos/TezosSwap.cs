using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Newtonsoft.Json.Linq;
using Serilog;

using Atomex.Abstract;
using Atomex.Blockchain.Abstract;
using Atomex.Blockchain.Tezos;
using Atomex.Common;
using Atomex.Core;
using Atomex.Swaps.Abstract;
using Atomex.Swaps.Helpers;
using Atomex.Swaps.Tezos.Helpers;
using Atomex.Wallet.Tezos;

namespace Atomex.Swaps.Tezos
{
    public class TezosSwap : CurrencySwap
    {
        protected const int MaxRedeemCheckAttempts = 10;
        protected const int MaxRefundCheckAttempts = 10;
        protected const int RedeemCheckAttemptIntervalInSec = 5;
        protected const int RefundCheckAttemptIntervalInSec = 5;
        protected static TimeSpan InitiationTimeout = TimeSpan.FromMinutes(10);
        protected static TimeSpan InitiationCheckInterval = TimeSpan.FromSeconds(15);

        private Atomex.Tezos Xtz => Currencies.Get<Atomex.Tezos>(Currency);
        protected readonly TezosAccount _account;

        public TezosSwap(
            TezosAccount account,
            ICurrencies currencies)
            : base(account.Currency, currencies)
        {
            _account = account ?? throw new ArgumentNullException(nameof(account));
        }

        public override async Task PayAsync(
            Swap swap,
            CancellationToken cancellationToken = default)
        {
            if (!await CheckPayRelevanceAsync(swap, cancellationToken))
                return;

            var lockTimeInSeconds = swap.IsInitiator
                ? DefaultInitiatorLockTimeInSeconds
                : DefaultAcceptorLockTimeInSeconds;

            var paymentTxs = (await CreatePaymentTxsAsync(swap, lockTimeInSeconds, cancellationToken)
                .ConfigureAwait(false))
                .ToList();

            if (paymentTxs.Count == 0)
            {
                Log.Error("Can't create payment transactions");
                return;
            }

            var isInitiateTx = true;

            try
            {
                foreach (var paymentTx in paymentTxs)
                {
                    var signResult = await SignTransactionAsync(paymentTx, cancellationToken)
                        .ConfigureAwait(false);

                    if (!signResult)
                    {
                        Log.Error("Transaction signing error");
                        return;
                    }

                    if (isInitiateTx)
                    {
                        swap.PaymentTx = paymentTx;
                        swap.StateFlags |= SwapStateFlags.IsPaymentSigned;

                        await UpdateSwapAsync(swap, SwapStateFlags.IsPaymentSigned, cancellationToken)
                            .ConfigureAwait(false);
                    }

                    await BroadcastTxAsync(swap, paymentTx, cancellationToken)
                            .ConfigureAwait(false);

                    if (isInitiateTx)
                    {
                        swap.PaymentTx = paymentTx;
                        swap.StateFlags |= SwapStateFlags.IsPaymentBroadcast;

                        await UpdateSwapAsync(swap, SwapStateFlags.IsPaymentBroadcast, cancellationToken)
                            .ConfigureAwait(false);

                        isInitiateTx = false;

                        // check initiate payment tx confirmation
                        if (paymentTxs.Count > 1)
                        {
                            var isInitiated = await WaitPaymentConfirmationAsync(paymentTx.Id, InitiationTimeout, cancellationToken)
                                .ConfigureAwait(false);

                            if (!isInitiated)
                            {
                                Log.Error("Initiation payment tx not confirmed after timeout {@timeout}", InitiationTimeout.Minutes);
                                return;
                            }
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Log.Error(e, "Swap payment error for swap {@swapId}", swap.Id);
                return;
            }
        }

        public override Task StartPartyPaymentControlAsync(
            Swap swap,
            CancellationToken cancellationToken = default)
        {
            Log.Debug("Start party payment control for swap {@swap}.", swap.Id);

            // initiator waits "accepted" event, acceptor waits "initiated" event
            var initiatedHandler = swap.IsInitiator
                ? new Func<Swap, CancellationToken, Task>(SwapAcceptedHandler)
                : new Func<Swap, CancellationToken, Task>(SwapInitiatedHandler);

            var lockTimeSeconds = swap.IsInitiator
                ? DefaultAcceptorLockTimeInSeconds
                : DefaultInitiatorLockTimeInSeconds;

            var refundTimeUtcInSec = new DateTimeOffset(swap.TimeStamp.ToUniversalTime().AddSeconds(lockTimeSeconds)).ToUnixTimeSeconds();

            TezosSwapInitiatedHelper.StartSwapInitiatedControlAsync(
                    swap: swap,
                    currency: Xtz,
                    refundTimeStamp: refundTimeUtcInSec,
                    interval: ConfirmationCheckInterval,
                    initiatedHandler: initiatedHandler,
                    canceledHandler: SwapCanceledHandler,
                    cancellationToken: cancellationToken)
                .FireAndForget();

            return Task.CompletedTask;
        }

        public override async Task RedeemAsync(
            Swap swap,
            CancellationToken cancellationToken = default)
        {
            var xtz = Xtz;

            var secretResult = await TezosSwapRedeemedHelper
                .IsRedeemedAsync(
                    swap: swap,
                    currency: xtz,
                    attempts: MaxRedeemCheckAttempts,
                    attemptIntervalInSec: RedeemCheckAttemptIntervalInSec,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (!secretResult.HasError && secretResult.Value != null)
            {
                await RedeemConfirmedEventHandler(swap, null, cancellationToken)
                    .ConfigureAwait(false);

                return;
            }

            if (swap.StateFlags.HasFlag(SwapStateFlags.IsRedeemBroadcast) &&
                swap.RedeemTx != null &&
                swap.RedeemTx.CreationTime != null &&
                swap.RedeemTx.CreationTime.Value.ToUniversalTime() + TimeSpan.FromMinutes(5) > DateTime.UtcNow)
            {
                // redeem already broadcast
                TrackTransactionConfirmationAsync(
                        swap: swap,
                        currency: xtz,
                        txId: swap.RedeemTx.Id,
                        confirmationHandler: RedeemConfirmedEventHandler,
                        cancellationToken: cancellationToken)
                    .FireAndForget();

                return;
            }

            // check already refunded by initiator
            if (swap.IsAcceptor &&
                swap.TimeStamp.ToUniversalTime().AddSeconds(DefaultInitiatorLockTimeInSeconds) < DateTime.UtcNow)
            {
                var isRefundedByParty = await TezosSwapRefundedHelper
                    .IsRefundedAsync(swap, xtz, cancellationToken)
                    .ConfigureAwait(false);

                if (isRefundedByParty != null &&
                    !isRefundedByParty.HasError &&
                    isRefundedByParty.Value)
                {
                    swap.StateFlags |= SwapStateFlags.IsUnsettled;

                    await UpdateSwapAsync(swap, SwapStateFlags.IsUnsettled, cancellationToken)
                        .ConfigureAwait(false);

                    return;
                }
            }

            if (swap.IsInitiator)
            {
                var redeemDeadline = swap.TimeStamp.ToUniversalTime().AddSeconds(DefaultAcceptorLockTimeInSeconds) - RedeemTimeReserve;

                if (DateTime.UtcNow > redeemDeadline)
                {
                    Log.Error("Redeem dedline reached for swap {@swap}", swap.Id);
                    return;
                }
            }

            Log.Debug("Create redeem for swap {@swapId}", swap.Id);

            var walletAddress = (await _account
                .GetUnspentAddressesAsync(
                    toAddress: swap.ToAddress,
                    amount: 0,
                    fee: 0,
                    feePrice: 0,
                    feeUsagePolicy: FeeUsagePolicy.EstimatedFee,
                    addressUsagePolicy: AddressUsagePolicy.UseOnlyOneAddress,
                    transactionType: BlockchainTransactionType.SwapRedeem,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false))
                .FirstOrDefault();

            if (walletAddress == null)
            {
                Log.Error("Insufficient funds for redeem");
                return;
            }

            var redeemTx = new TezosTransaction
            {
                Currency      = xtz,
                CreationTime  = DateTime.UtcNow,
                From          = walletAddress.Address,
                To            = xtz.SwapContractAddress,
                Amount        = 0,
                Fee           = xtz.RedeemFee + xtz.RevealFee,
                GasLimit      = xtz.RedeemGasLimit,
                StorageLimit  = xtz.RedeemStorageLimit,
                Params        = RedeemParams(swap),
                UseDefaultFee = true,
                Type          = BlockchainTransactionType.Output | BlockchainTransactionType.SwapRedeem
            };

            var signResult = await SignTransactionAsync(redeemTx, cancellationToken)
                .ConfigureAwait(false);

            if (!signResult)
            {
                Log.Error("Transaction signing error");
                return;
            }

            swap.RedeemTx = redeemTx;
            swap.StateFlags |= SwapStateFlags.IsRedeemSigned;

            await UpdateSwapAsync(swap, SwapStateFlags.IsRedeemSigned, cancellationToken)
                .ConfigureAwait(false);

            await BroadcastTxAsync(swap, redeemTx, cancellationToken)
                .ConfigureAwait(false);

            swap.RedeemTx = redeemTx;
            swap.StateFlags |= SwapStateFlags.IsRedeemBroadcast;

            await UpdateSwapAsync(swap, SwapStateFlags.IsRedeemBroadcast, cancellationToken)
                .ConfigureAwait(false);

            TrackTransactionConfirmationAsync(
                    swap: swap,
                    currency: xtz,
                    txId: redeemTx.Id,
                    confirmationHandler: RedeemConfirmedEventHandler,
                    cancellationToken: cancellationToken)
                .FireAndForget();
        }

        public override async Task RedeemForPartyAsync(
            Swap swap,
            CancellationToken cancellationToken = default)
        {
            var xtz = Xtz;

            if (swap.IsInitiator)
            {
                var partyRedeemDeadline = swap.TimeStamp.ToUniversalTime().AddSeconds(DefaultAcceptorLockTimeInSeconds) - PartyRedeemTimeReserve;

                if (DateTime.UtcNow > partyRedeemDeadline)
                {
                    Log.Error("Party redeem dedline reached for swap {@swap}", swap.Id);
                    return;
                }
            }

            Log.Debug("Create redeem for acceptor for swap {@swapId}", swap.Id);

            var walletAddress = (await _account
                .GetUnspentAddressesAsync(
                    toAddress: swap.PartyAddress, // todo: check it
                    amount: 0,
                    fee: 0,
                    feePrice: 0,
                    feeUsagePolicy: FeeUsagePolicy.EstimatedFee,
                    addressUsagePolicy: AddressUsagePolicy.UseOnlyOneAddress,
                    transactionType: BlockchainTransactionType.SwapRedeem,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false))
                .FirstOrDefault();

            if (walletAddress == null)
            {
                Log.Error("Insufficient balance for party redeem. Cannot find the address containing the required amount of funds.");
                return;
            }

            var redeemTx = new TezosTransaction
            {
                Currency      = xtz,
                CreationTime  = DateTime.UtcNow,
                From          = walletAddress.Address,
                To            = xtz.SwapContractAddress,
                Amount        = 0,
                Fee           = xtz.RedeemFee + xtz.RevealFee,
                GasLimit      = xtz.RedeemGasLimit,
                StorageLimit  = xtz.RedeemStorageLimit,
                Params        = RedeemParams(swap),
                UseDefaultFee = true,
                Type          = BlockchainTransactionType.Output | BlockchainTransactionType.SwapRedeem
            };

            var signResult = await SignTransactionAsync(redeemTx, cancellationToken)
                .ConfigureAwait(false);

            if (!signResult)
            {
                Log.Error("Transaction signing error");
                return;
            }

            await BroadcastTxAsync(swap, redeemTx, cancellationToken)
                .ConfigureAwait(false);
        }

        public override async Task RefundAsync(
            Swap swap,
            CancellationToken cancellationToken = default)
        {
            var xtz = Xtz;

            if (swap.StateFlags.HasFlag(SwapStateFlags.IsRefundBroadcast) &&
                swap.RefundTx != null &&
                swap.RefundTx.CreationTime != null &&
                swap.RefundTx.CreationTime.Value.ToUniversalTime() + TimeSpan.FromMinutes(5) > DateTime.UtcNow)
            {
                TrackTransactionConfirmationAsync(
                        swap: swap,
                        currency: xtz,
                        txId: swap.RefundTx.Id,
                        confirmationHandler: RefundConfirmedEventHandler,
                        cancellationToken: cancellationToken)
                    .FireAndForget();

                return;
            }

            Log.Debug("Create refund for swap {@swap}", swap.Id);

            var walletAddress = (await _account
                .GetUnspentAddressesAsync(
                    toAddress: null, // todo: get refund address
                    amount: 0,
                    fee: 0,
                    feePrice: 0,
                    feeUsagePolicy: FeeUsagePolicy.EstimatedFee,
                    addressUsagePolicy: AddressUsagePolicy.UseOnlyOneAddress,
                    transactionType: BlockchainTransactionType.SwapRefund,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false))
                .FirstOrDefault();

            if (walletAddress == null)
            {
                Log.Error("Insufficient funds for refund");
                return;
            }

            var refundTx = new TezosTransaction   //todo: use estimated fee and storage limit
            {
                Currency      = xtz,
                CreationTime  = DateTime.UtcNow,
                From          = walletAddress.Address,
                To            = xtz.SwapContractAddress,
                Fee           = xtz.RefundFee + xtz.RevealFee,
                GasLimit      = xtz.RefundGasLimit,
                StorageLimit  = xtz.RefundStorageLimit,
                Params        = RefundParams(swap),
                UseDefaultFee = true,
                Type          = BlockchainTransactionType.Output | BlockchainTransactionType.SwapRefund
            };

            var signResult = await SignTransactionAsync(refundTx, cancellationToken)
                .ConfigureAwait(false);

            if (!signResult)
            {
                Log.Error("Transaction signing error");
                return;
            }

            swap.RefundTx = refundTx;
            swap.StateFlags |= SwapStateFlags.IsRefundSigned;

            await UpdateSwapAsync(swap, SwapStateFlags.IsRefundSigned, cancellationToken)
                .ConfigureAwait(false);

            await BroadcastTxAsync(swap, refundTx, cancellationToken)
                .ConfigureAwait(false);

            swap.RefundTx = refundTx;
            swap.StateFlags |= SwapStateFlags.IsRefundBroadcast;

            await UpdateSwapAsync(swap, SwapStateFlags.IsRefundBroadcast, cancellationToken)
                .ConfigureAwait(false);

            TrackTransactionConfirmationAsync(
                    swap: swap,
                    currency: xtz,
                    txId: refundTx.Id,
                    confirmationHandler: RefundConfirmedEventHandler,
                    cancellationToken: cancellationToken)
                .FireAndForget();
        }

        public override Task StartWaitForRedeemAsync(
            Swap swap,
            CancellationToken cancellationToken = default)
        {
            var lockTimeInSeconds = swap.IsInitiator
                ? DefaultInitiatorLockTimeInSeconds
                : DefaultAcceptorLockTimeInSeconds;

            // start redeem control async
            TezosSwapRedeemedHelper.StartSwapRedeemedControlAsync(
                    swap: swap,
                    currency: Xtz,
                    refundTimeUtc: swap.TimeStamp.ToUniversalTime().AddSeconds(lockTimeInSeconds),
                    interval: TimeSpan.FromSeconds(30),
                    cancelOnlyIfRefundTimeReached: true,
                    redeemedHandler: RedeemCompletedEventHandler,
                    canceledHandler: RedeemCanceledEventHandler,
                    cancellationToken: cancellationToken)
                .FireAndForget();

            return Task.CompletedTask;
        }

        public override Task StartWaitForRedeemBySomeoneAsync(
            Swap swap,
            CancellationToken cancellationToken = default)
        {
            Log.Debug("Wait redeem for swap {@swapId}", swap.Id);

            // start redeem control async
            TezosSwapRedeemedHelper.StartSwapRedeemedControlAsync(
                    swap: swap,
                    currency: Xtz,
                    refundTimeUtc: swap.TimeStamp.ToUniversalTime().AddSeconds(DefaultAcceptorLockTimeInSeconds),
                    interval: TimeSpan.FromSeconds(30),
                    cancelOnlyIfRefundTimeReached: true,
                    redeemedHandler: RedeemBySomeoneCompletedEventHandler,
                    canceledHandler: RedeemBySomeoneCanceledEventHandler,
                    cancellationToken: cancellationToken)
                .FireAndForget();

            return Task.CompletedTask;
        }

        public override async Task<Result<IBlockchainTransaction>> TryToFindPaymentAsync(
            Swap swap,
            CancellationToken cancellationToken = default)
        {
            var currency = Currencies
                .GetByName(swap.SoldCurrency);

            return await TezosSwapInitiatedHelper
                .TryToFindPaymentAsync(
                    swap: swap,
                    currency: currency,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }

        #region Event Handlers

        protected override async Task RefundTimeReachedHandler(
            Swap swap,
            CancellationToken cancellationToken = default)
        {
            Log.Debug("Refund time reached for swap {@swapId}", swap.Id);

            try
            {
                var isRefundedResult = await TezosSwapRefundedHelper.IsRefundedAsync(
                        swap: swap,
                        currency: Xtz,
                        attempts: MaxRefundCheckAttempts,
                        attemptIntervalInSec: RefundCheckAttemptIntervalInSec,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                if (!isRefundedResult.HasError)
                {
                    if (isRefundedResult.Value)
                    {
                        await RefundConfirmedEventHandler(swap, swap.RefundTx, cancellationToken)
                            .ConfigureAwait(false);
                    }
                    else
                    {
                        await RefundAsync(swap, cancellationToken)
                            .ConfigureAwait(false);
                    }
                }
            }
            catch (Exception e)
            {
                Log.Error(e, "Error in refund time reached handler");
            }
        }

        private async Task RedeemBySomeoneCompletedEventHandler(
            Swap swap,
            byte[] secret,
            CancellationToken cancellationToken = default)
        {
            Log.Debug("Handle redeem party control completed event for swap {@swapId}", swap.Id);

            if (swap.IsAcceptor)
            {
                swap.Secret = secret;
                swap.StateFlags |= SwapStateFlags.IsRedeemConfirmed;

                await UpdateSwapAsync(swap, SwapStateFlags.IsRedeemConfirmed, cancellationToken)
                    .ConfigureAwait(false);

                // get transactions & update balance for address async 
                AddressHelper.UpdateAddressBalanceAsync<TezosWalletScanner, TezosAccount>(
                        account: _account,
                        address: swap.ToAddress,
                        cancellationToken: cancellationToken)
                    .FireAndForget();
            }
        }

        private async Task RedeemBySomeoneCanceledEventHandler(
            Swap swap,
            DateTime refundTimeUtc,
            CancellationToken cancellationToken = default)
        {
            Log.Debug("Handle redeem party control canceled event for swap {@swapId}", swap.Id);

            try
            {
                if (swap.Secret?.Length > 0)
                {
                    var walletAddress = (await _account
                        .GetUnspentAddressesAsync(
                            toAddress: swap.ToAddress,
                            amount: 0,
                            fee: 0,
                            feePrice: 0,
                            feeUsagePolicy: FeeUsagePolicy.EstimatedFee,
                            addressUsagePolicy: AddressUsagePolicy.UseOnlyOneAddress,
                            transactionType: BlockchainTransactionType.SwapRedeem,
                            cancellationToken: cancellationToken)
                        .ConfigureAwait(false))
                        .FirstOrDefault();

                    if (walletAddress == null) //todo: make some panic here
                    {
                        Log.Error(
                            "Counter counterParty redeem need to be made for swap {@swapId}, using secret {@Secret}",
                            swap.Id,
                            Convert.ToBase64String(swap.Secret));
                        return;
                    }

                    await RedeemAsync(swap, cancellationToken)
                        .ConfigureAwait(false);
                }
            }
            catch (Exception e)
            {
                Log.Error(e, "Redeem party control canceled event error");
            }
        }

        #endregion Event Handlers

        #region Helpers

        protected virtual async Task<IEnumerable<TezosTransaction>> CreatePaymentTxsAsync(
            Swap swap,
            int lockTimeSeconds,
            CancellationToken cancellationToken = default)
        {
            var xtz = Xtz;

            Log.Debug("Create payment transactions for swap {@swapId}", swap.Id);

            var requiredAmountInMtz = AmountHelper
                .QtyToAmount(swap.Side, swap.Qty, swap.Price, xtz.DigitsMultiplier)
                .ToMicroTez();

            // maker network fee
            if (swap.MakerNetworkFee > 0)
            {
                var makerNetworkFeeInMtz = swap.MakerNetworkFee.ToMicroTez();

                if (makerNetworkFeeInMtz < requiredAmountInMtz) // network fee size check
                    requiredAmountInMtz += makerNetworkFeeInMtz;
            }

            var refundTimeStampUtcInSec = new DateTimeOffset(swap.TimeStamp.ToUniversalTime().AddSeconds(lockTimeSeconds)).ToUnixTimeSeconds();
            var isInitTx = true;
            var rewardForRedeemInMtz = swap.IsInitiator
                ? swap.PartyRewardForRedeem.ToMicroTez()
                : 0;

            var unspentAddresses = (await _account
                .GetUnspentAddressesAsync(cancellationToken)
                .ConfigureAwait(false))
                .ToList()
                .SortList(new AvailableBalanceAscending());

            var transactions = new List<TezosTransaction>();

            foreach (var walletAddress in unspentAddresses)
            {
                Log.Debug("Create swap payment tx from address {@address} for swap {@swapId}",
                    walletAddress.Address,
                    swap.Id);

                var balanceInTz = (await _account
                    .GetAddressBalanceAsync(
                        address: walletAddress.Address,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false))
                    .Available;

                Log.Debug("Available balance: {@balance}", balanceInTz);

                var balanceInMtz = balanceInTz.ToMicroTez();

                var isRevealed = await _account
                    .IsRevealedSourceAsync(walletAddress.Address, cancellationToken)
                    .ConfigureAwait(false);

                var feeAmountInMtz = isInitTx
                    ? xtz.InitiateFee + (isRevealed ? 0 : xtz.RevealFee)
                    : xtz.AddFee + (isRevealed ? 0 : xtz.RevealFee);

                var storageLimitInMtz = isInitTx
                    ? xtz.InitiateStorageLimit * xtz.StorageFeeMultiplier
                    : xtz.AddStorageLimit * xtz.StorageFeeMultiplier;

                var amountInMtz = Math.Min(balanceInMtz - feeAmountInMtz - storageLimitInMtz, requiredAmountInMtz);

                if (amountInMtz <= 0)
                {
                    Log.Warning(
                        "Insufficient funds at {@address}. Balance: {@balance}, " +
                        "feeAmount: {@feeAmount}, storageLimit: {@storageLimit}, result: {@result}.",
                        walletAddress.Address,
                        balanceInMtz,
                        feeAmountInMtz,
                        storageLimitInMtz,
                        amountInMtz);

                    continue;
                }

                requiredAmountInMtz -= amountInMtz;

                if (isInitTx)
                {
                    transactions.Add(new TezosTransaction
                    {
                        Currency      = xtz,
                        CreationTime  = DateTime.UtcNow,
                        From          = walletAddress.Address,
                        To            = xtz.SwapContractAddress,
                        Amount        = Math.Round(amountInMtz, 0),
                        Fee           = feeAmountInMtz,
                        GasLimit      = xtz.InitiateGasLimit,
                        StorageLimit  = xtz.InitiateStorageLimit,
                        Params        = InitParams(swap, refundTimeStampUtcInSec, (long)rewardForRedeemInMtz),
                        UseDefaultFee = true,
                        Type          = BlockchainTransactionType.Output | BlockchainTransactionType.SwapPayment
                    });
                }
                else
                {
                    transactions.Add(new TezosTransaction
                    {
                        Currency      = xtz,
                        CreationTime  = DateTime.UtcNow,
                        From          = walletAddress.Address,
                        To            = xtz.SwapContractAddress,
                        Amount        = Math.Round(amountInMtz, 0),
                        Fee           = feeAmountInMtz,
                        GasLimit      = xtz.AddGasLimit,
                        StorageLimit  = xtz.AddStorageLimit,
                        UseDefaultFee = true,
                        Params        = AddParams(swap),
                        Type          = BlockchainTransactionType.Output | BlockchainTransactionType.SwapPayment
                    });
                }

                if (isInitTx)
                    isInitTx = false;

                if (requiredAmountInMtz == 0)
                    break;
            }

            if (requiredAmountInMtz > 0)
            {
                Log.Warning("Insufficient funds (left {@requredAmount}).", requiredAmountInMtz);
                return Enumerable.Empty<TezosTransaction>();
            }

            return transactions;
        }

        private async Task<bool> SignTransactionAsync(
            TezosTransaction tx,
            CancellationToken cancellationToken = default)
        {
            var walletAddress = await _account
                .GetAddressAsync(
                    address: tx.From,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            return await _account.Wallet
                .SignAsync(
                    tx: tx,
                    address: walletAddress,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }

        private async Task BroadcastTxAsync(
            Swap swap,
            TezosTransaction tx,
            CancellationToken cancellationToken = default)
        {
            var broadcastResult = await Xtz.BlockchainApi
                .TryBroadcastAsync(tx, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (broadcastResult.HasError)
                throw new Exception($"Error while broadcast transaction with code {broadcastResult.Error.Code} and description {broadcastResult.Error.Description}");

            var txId = broadcastResult.Value;

            if (txId == null)
                throw new Exception("Transaction Id is null");

            Log.Debug("TxId {@id} for swap {@swapId}", txId, swap.Id);

            // account new unconfirmed transaction
            await _account
                .UpsertTransactionAsync(
                    tx: tx,
                    updateBalance: true,
                    notifyIfUnconfirmed: true,
                    notifyIfBalanceUpdated: true,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            // todo: transaction receipt status control
        }

        private async Task<bool> WaitPaymentConfirmationAsync(
            string txId,
            TimeSpan timeout,
            CancellationToken cancellationToken = default)
        {
            var timeStamp = DateTime.UtcNow;

            while (DateTime.UtcNow < timeStamp + timeout)
            {
                await Task.Delay(InitiationCheckInterval, cancellationToken)
                    .ConfigureAwait(false);

                var tx = await Xtz.BlockchainApi
                    .TryGetTransactionAsync(txId, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                if (tx != null && !tx.HasError && tx.Value != null && tx.Value.State == BlockchainTransactionState.Confirmed)
                    return true;
            }

            return false;
        }

        private JObject InitParams(
            Swap swap,
            long refundTimestamp,
            long redeemFeeAmount)
        {
            return JObject.Parse(@"{'entrypoint':'default','value':{'prim':'Left','args':[{'prim':'Left','args':[{'prim':'Pair','args':[{'string':'" + swap.PartyAddress + "'},{'prim':'Pair','args':[{'prim':'Pair','args':[{'bytes':'" + swap.SecretHash.ToHexString() + "'},{'int':'" + refundTimestamp + "'}]},{'int':'" + redeemFeeAmount + "'}]}]}]}]}}");
        }

        private JObject AddParams(Swap swap)
        {
            return JObject.Parse(@"{'entrypoint':'default','value':{'prim':'Left','args':[{'prim':'Right','args':[{'bytes':'" + swap.SecretHash.ToHexString() + "'}]}]}}");
        }

        private JObject RedeemParams(Swap swap)
        {
            return JObject.Parse(@"{'entrypoint':'default','value':{'prim':'Right','args':[{'prim':'Left','args':[{'bytes':'" + swap.Secret.ToHexString() + "'}]}]}}");
        }

        private JObject RefundParams(Swap swap)
        {
            return JObject.Parse(@"{'entrypoint':'default','value':{'prim':'Right','args':[{'prim':'Right','args':[{'bytes':'" + swap.SecretHash.ToHexString() + "'}]}]}}");
        }

        #endregion Helpers
    }
}