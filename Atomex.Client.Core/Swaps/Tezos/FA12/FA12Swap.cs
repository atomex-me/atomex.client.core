using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Atomex.Abstract;
using Atomex.Blockchain.Abstract;
using Atomex.Blockchain.Tezos;
using Atomex.Common;
using Atomex.Core;
using Atomex.Swaps.Abstract;
using Atomex.Swaps.Helpers;
using Atomex.Swaps.Tezos.FA12.Helpers;
using Atomex.Wallet.Tezos;
using Newtonsoft.Json.Linq;
using Serilog;

namespace Atomex.Swaps.Tezos.FA12
{
    public class FA12Swap : TezosSwap
    {
        private FA12Account Fa12Account => _account as FA12Account;
        private TezosAccount TezosAccount { get; }
        private TezosTokens.FA12 Fa12 => Currencies.Get<TezosTokens.FA12>(Currency);
        private Atomex.Tezos Xtz => Currencies.Get<Atomex.Tezos>(TezosAccount.Currency);

        public FA12Swap(
            FA12Account account,
            TezosAccount tezosAccount,
            ISwapClient swapClient,
            ICurrencies currencies)
            : base(account, swapClient, currencies)
        {
            TezosAccount = tezosAccount ?? throw new ArgumentNullException(nameof(account));
        }

        public override async Task PayAsync(
            Swap swap,
            CancellationToken cancellationToken = default)
        {
            if (!CheckPayRelevance(swap))
                return;

            var lockTimeInSeconds = swap.IsInitiator
                ? DefaultInitiatorLockTimeInSeconds
                : DefaultAcceptorLockTimeInSeconds;

            var txs = (await CreatePaymentTxsAsync(swap, lockTimeInSeconds, cancellationToken)
                .ConfigureAwait(false))
                .ToList();

            if (txs.Count == 0)
            {
                Log.Error("Can't create payment transactions");
                return;
            }

            try
            {
                var approvalTxs = txs
                    .Where(tx => tx.Type.HasFlag(BlockchainTransactionType.TokenApprove))
                    .ToList();

                foreach (var approvalTx in approvalTxs)
                {
                    var signResult = await SignTransactionAsync(approvalTx, cancellationToken)
                        .ConfigureAwait(false);

                    if (!signResult)
                    {
                        Log.Error("Approve transaction signing error");
                        return;
                    }

                    await BroadcastTxAsync(swap, approvalTx, cancellationToken, false, true, false)
                        .ConfigureAwait(false);

                    var isApproved = await WaitPaymentConfirmationAsync(
                            txId: approvalTx.Id,
                            timeout: InitiationTimeout,
                            cancellationToken: cancellationToken)
                        .ConfigureAwait(false);

                    if (!isApproved)
                    {
                        Log.Error("Approve payment txs not confirmed after timeout {@timeout}", InitiationTimeout.Minutes);
                        return;
                    }
                }

                txs = txs
                    .Where(tx => tx.Type.HasFlag(BlockchainTransactionType.SwapPayment))
                    .ToList();

                var isInitiateTx = true;

                foreach (var tx in txs)
                {
                    var signResult = await SignTransactionAsync(tx, cancellationToken)
                        .ConfigureAwait(false);

                    if (!signResult)
                    {
                        Log.Error("Transaction signing error");
                        return;
                    }

                    if (isInitiateTx)
                    {
                        swap.PaymentTx = tx;
                        swap.StateFlags |= SwapStateFlags.IsPaymentSigned;
                        RaiseSwapUpdated(swap, SwapStateFlags.IsPaymentSigned);
                    }

                    await BroadcastTxAsync(swap, tx, cancellationToken)
                        .ConfigureAwait(false);

                    if (isInitiateTx)
                    {
                        swap.PaymentTx = tx;
                        swap.StateFlags |= SwapStateFlags.IsPaymentBroadcast;
                        RaiseSwapUpdated(swap, SwapStateFlags.IsPaymentBroadcast);

                        isInitiateTx = false;

                        // check initiate payment tx confirmation
                        if (txs.Count > 1)
                        {
                            var isInitiated = await WaitPaymentConfirmationAsync(
                                    txId: tx.Id,
                                    timeout: InitiationTimeout,
                                    cancellationToken: cancellationToken)
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

            if (swap.StateFlags.HasFlag(SwapStateFlags.IsPaymentBroadcast))
            {
                // start redeem control async
                await StartWaitForRedeemAsync(swap, cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        public override Task StartPartyPaymentControlAsync(
            Swap swap,
            CancellationToken cancellationToken = default)
        {
            // initiator waits "accepted" event, acceptor waits "initiated" event
            var initiatedHandler = swap.IsInitiator
                ? new Action<Swap, CancellationToken>(SwapAcceptedEventHandler)
                : new Action<Swap, CancellationToken>(SwapInitiatedEventHandler);

            var lockTimeSeconds = swap.IsInitiator
                ? DefaultAcceptorLockTimeInSeconds
                : DefaultInitiatorLockTimeInSeconds;

            var refundTimeUtcInSec = new DateTimeOffset(swap.TimeStamp.ToUniversalTime().AddSeconds(lockTimeSeconds)).ToUnixTimeSeconds();

            FA12SwapInitiatedHelper.StartSwapInitiatedControlAsync(
                    swap: swap,
                    currency: Fa12,
                    tezos: Xtz,
                    refundTimeStamp: refundTimeUtcInSec,
                    interval: ConfirmationCheckInterval,
                    initiatedHandler: initiatedHandler,
                    canceledHandler: SwapCanceledEventHandler,
                    cancellationToken: cancellationToken)
                .FireAndForget();

            return Task.CompletedTask;
        }

        public override async Task RedeemAsync(
            Swap swap,
            CancellationToken cancellationToken = default)
        {
            var fa12 = Fa12;
            
            var secretResult = await FA12SwapRedeemedHelper
                .IsRedeemedAsync(
                    swap: swap,
                    currency: fa12,
                    tezos: Xtz,
                    attempts: MaxRedeemCheckAttempts,
                    attemptIntervalInSec: RedeemCheckAttemptIntervalInSec,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (!secretResult.HasError && secretResult.Value != null)
            {
                RedeemConfirmedEventHandler(swap, null, cancellationToken);
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
                        currency: fa12,
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
                var isRefundedByParty = await FA12SwapRefundedHelper
                    .IsRefundedAsync(swap, fa12, Xtz, cancellationToken)
                    .ConfigureAwait(false);

                if (isRefundedByParty != null &&
                    !isRefundedByParty.HasError &&
                    isRefundedByParty.Value)
                {
                    swap.StateFlags |= SwapStateFlags.IsUnsettled;
                    RaiseSwapUpdated(swap, SwapStateFlags.IsUnsettled);
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

            var walletAddress = (await Fa12Account
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
                Currency = fa12,
                CreationTime = DateTime.UtcNow,
                From = walletAddress.Address,
                To = fa12.SwapContractAddress,
                Amount = 0,
                Fee = fa12.RedeemFee + fa12.RevealFee,
                GasLimit = fa12.RedeemGasLimit,
                StorageLimit = fa12.RedeemStorageLimit,
                Params = RedeemParams(swap),
                UseDefaultFee = true,
                Type = BlockchainTransactionType.Output | BlockchainTransactionType.SwapRedeem
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
            RaiseSwapUpdated(swap, SwapStateFlags.IsRedeemSigned);

            await BroadcastTxAsync(swap, redeemTx, cancellationToken)
                .ConfigureAwait(false);

            swap.RedeemTx = redeemTx;
            swap.StateFlags |= SwapStateFlags.IsRedeemBroadcast;
            RaiseSwapUpdated(swap, SwapStateFlags.IsRedeemBroadcast);

            TrackTransactionConfirmationAsync(
                    swap: swap,
                    currency: fa12,
                    txId: redeemTx.Id,
                    confirmationHandler: RedeemConfirmedEventHandler,
                    cancellationToken: cancellationToken)
                .FireAndForget();
        }

        public override async Task RedeemForPartyAsync(
            Swap swap,
            CancellationToken cancellationToken = default)
        {
            var fa12 = Fa12;

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

            var walletAddress = (await Fa12Account
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
                Currency = fa12,
                CreationTime = DateTime.UtcNow,
                From = walletAddress.Address,
                To = fa12.SwapContractAddress,
                Amount = 0,
                Fee = fa12.RedeemFee + fa12.RevealFee,
                GasLimit = fa12.RedeemGasLimit,
                StorageLimit = fa12.RedeemStorageLimit,
                Params = RedeemParams(swap),
                UseDefaultFee = true,
                Type = BlockchainTransactionType.Output | BlockchainTransactionType.SwapRedeem
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
            var fa12 = Fa12;

            if (swap.StateFlags.HasFlag(SwapStateFlags.IsRefundBroadcast) &&
                swap.RefundTx != null &&
                swap.RefundTx.CreationTime != null &&
                swap.RefundTx.CreationTime.Value.ToUniversalTime() + TimeSpan.FromMinutes(5) > DateTime.UtcNow)
            {
                TrackTransactionConfirmationAsync(
                        swap: swap,
                        currency: fa12,
                        txId: swap.RefundTx.Id,
                        confirmationHandler: RefundConfirmedEventHandler,
                        cancellationToken: cancellationToken)
                    .FireAndForget();

                return;
            }

            Log.Debug("Create refund for swap {@swap}", swap.Id);

            var walletAddress = (await Fa12Account
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
                Currency = fa12,
                CreationTime = DateTime.UtcNow,
                From = walletAddress.Address,
                To = fa12.SwapContractAddress,
                Fee = fa12.RefundFee + fa12.RevealFee,
                GasLimit = fa12.RefundGasLimit,
                StorageLimit = fa12.RefundStorageLimit,
                Params = RefundParams(swap),
                UseDefaultFee = true,
                Type = BlockchainTransactionType.Output | BlockchainTransactionType.SwapRefund
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
            RaiseSwapUpdated(swap, SwapStateFlags.IsRefundSigned);

            await BroadcastTxAsync(swap, refundTx, cancellationToken)
                .ConfigureAwait(false);

            swap.RefundTx = refundTx;
            swap.StateFlags |= SwapStateFlags.IsRefundBroadcast;
            RaiseSwapUpdated(swap, SwapStateFlags.IsRefundBroadcast);

            TrackTransactionConfirmationAsync(
                    swap: swap,
                    currency: fa12,
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
            FA12SwapRedeemedHelper.StartSwapRedeemedControlAsync(
                    swap: swap,
                    currency: Fa12,
                    tezos: Xtz,
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
            FA12SwapRedeemedHelper.StartSwapRedeemedControlAsync(
                    swap: swap,
                    currency: Fa12,
                    tezos: Xtz,
                    refundTimeUtc: swap.TimeStamp.ToUniversalTime().AddSeconds(DefaultAcceptorLockTimeInSeconds),
                    interval: TimeSpan.FromSeconds(30),
                    cancelOnlyIfRefundTimeReached: true,
                    redeemedHandler: RedeemBySomeoneCompletedEventHandler,
                    canceledHandler: RedeemBySomeoneCanceledEventHandler,
                    cancellationToken: cancellationToken)
                .FireAndForget();

            return Task.CompletedTask;
        }

        #region Event Handlers

        private void SwapInitiatedEventHandler(
            Swap swap,
            CancellationToken cancellationToken = default)
        {
            Log.Debug(
                "Initiator's payment transaction received. Now acceptor can broadcast payment tx for swap {@swapId}",
                swap.Id);

            swap.StateFlags |= SwapStateFlags.HasPartyPayment;
            swap.StateFlags |= SwapStateFlags.IsPartyPaymentConfirmed;
            RaiseSwapUpdated(swap, SwapStateFlags.HasPartyPayment | SwapStateFlags.IsPartyPaymentConfirmed);

            InitiatorPaymentConfirmed?.Invoke(this, new SwapEventArgs(swap));
        }

        private async void SwapAcceptedEventHandler(
            Swap swap,
            CancellationToken cancellationToken = default)
        {
            try
            {
                Log.Debug(
                    "Acceptor's payment transaction received. Now initiator can do self redeem and do party redeem for acceptor (if needs and wants) for swap {@swapId}.",
                    swap.Id);

                swap.StateFlags |= SwapStateFlags.HasPartyPayment;
                swap.StateFlags |= SwapStateFlags.IsPartyPaymentConfirmed;
                RaiseSwapUpdated(swap, SwapStateFlags.HasPartyPayment | SwapStateFlags.IsPartyPaymentConfirmed);

                RaiseAcceptorPaymentConfirmed(swap);

                await RedeemAsync(swap, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception e)
            {
                Log.Error(e, "Swap accepted error");
            }
        }

        private void SwapCanceledEventHandler(
            Swap swap,
            CancellationToken cancellationToken = default)
        {
            // todo: do smth here
            Log.Debug("Swap canceled due to wrong counterParty params {@swapId} or timeout", swap.Id);
        }

        private void RedeemConfirmedEventHandler(
            Swap swap,
            IBlockchainTransaction tx,
            CancellationToken cancellationToken = default)
        {
            swap.StateFlags |= SwapStateFlags.IsRedeemConfirmed;
            RaiseSwapUpdated(swap, SwapStateFlags.IsRedeemConfirmed);
        }

        private void RedeemCompletedEventHandler(
            Swap swap,
            byte[] secret,
            CancellationToken cancellationToken = default)
        {
            Log.Debug("Handle redeem control completed event for swap {@swapId}", swap.Id);

            if (swap.IsAcceptor)
            {
                swap.Secret = secret;
                RaiseSwapUpdated(swap, SwapStateFlags.HasSecret);

                RaiseAcceptorPaymentSpent(swap);
            }
        }

        private void RedeemCanceledEventHandler(
            Swap swap,
            DateTime refundTimeUtc,
            CancellationToken cancellationToken = default)
        {
            Log.Debug("Handle redeem control canceled event for swap {@swapId}", swap.Id);

            ControlRefundTimeAsync(
                    swap: swap,
                    refundTimeUtc: refundTimeUtc,
                    refundTimeReachedHandler: RefundTimeReachedEventHandler,
                    cancellationToken: cancellationToken)
                .FireAndForget();
        }

        private async void RefundTimeReachedEventHandler(
            Swap swap,
            CancellationToken cancellationToken = default)
        {
            Log.Debug("Refund time reached for swap {@swapId}", swap.Id);

            try
            {
                var isRefundedResult = await FA12SwapRefundedHelper.IsRefundedAsync(
                        swap: swap,
                        currency: Fa12,
                        tezos: Xtz,
                        attempts: MaxRefundCheckAttempts,
                        attemptIntervalInSec: RefundCheckAttemptIntervalInSec,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                if (!isRefundedResult.HasError)
                {
                    if (isRefundedResult.Value)
                    {
                        RefundConfirmedEventHandler(swap, swap.RefundTx, cancellationToken);
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

        private void RefundConfirmedEventHandler(
            Swap swap,
            IBlockchainTransaction tx,
            CancellationToken cancellationToken = default)
        {
            swap.StateFlags |= SwapStateFlags.IsRefundConfirmed;
            RaiseSwapUpdated(swap, SwapStateFlags.IsRefundConfirmed);
        }

        private void RedeemBySomeoneCompletedEventHandler(
            Swap swap,
            byte[] secret,
            CancellationToken cancellationToken = default)
        {
            Log.Debug("Handle redeem party control completed event for swap {@swapId}", swap.Id);

            if (swap.IsAcceptor)
            {
                swap.Secret = secret;
                swap.StateFlags |= SwapStateFlags.IsRedeemConfirmed;
                RaiseSwapUpdated(swap, SwapStateFlags.IsRedeemConfirmed);

                // get transactions & update balance for address async 
                AddressHelper.UpdateAddressBalanceAsync<TezosWalletScanner, TezosAccount>(
                        account: _account,
                        address: swap.ToAddress,
                        cancellationToken: cancellationToken)
                    .FireAndForget();
            }
        }

        private async void RedeemBySomeoneCanceledEventHandler(
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

        protected override async Task<IEnumerable<TezosTransaction>> CreatePaymentTxsAsync(
            Swap swap,
            int lockTimeSeconds,
            CancellationToken cancellationToken = default)
        {
            Log.Debug("Create payment transactions for swap {@swapId}", swap.Id);

            var fa12 = Fa12;
            var fa12Api = fa12.BlockchainApi as ITokenBlockchainApi;

            var requiredAmountInTokens = AmountHelper.QtyToAmount(swap.Side, swap.Qty, swap.Price, fa12.DigitsMultiplier);

            var refundTimeStampUtcInSec = new DateTimeOffset(swap.TimeStamp.ToUniversalTime().AddSeconds(lockTimeSeconds)).ToUnixTimeSeconds();
            var isInitTx = true;
            var rewardForRedeemInTokenDigits = swap.IsInitiator
                ? swap.PartyRewardForRedeem.ToTokenDigits(fa12.DigitsMultiplier)
                : 0;

            var unspentAddresses = (await Fa12Account
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

                var balanceInTz = (await TezosAccount
                    .GetAddressBalanceAsync(
                        address: walletAddress.Address,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false))
                    .Available;

                var balanceInTokens = (await Fa12Account
                    .GetAddressBalanceAsync(
                        address: walletAddress.Address,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false))
                    .Available;

                Log.Debug("Available balance: {@balance}", balanceInTokens);

                var balanceInMtz = balanceInTz.ToMicroTez();
                var balanceInTokenDigits = balanceInTokens.ToTokenDigits(fa12.DigitsMultiplier);

                var isRevealed = await _account
                    .IsRevealedSourceAsync(walletAddress.Address, cancellationToken)
                    .ConfigureAwait(false);

                var feeAmountInMtz = fa12.ApproveFee * 2 +
                    (isInitTx ? fa12.InitiateFee : fa12.AddFee) +
                    (isRevealed ? 0 : fa12.RevealFee);

                var storageLimitInMtz = (fa12.ApproveStorageLimit * 2 +
                    (isInitTx ? fa12.InitiateStorageLimit : fa12.AddStorageLimit)) *
                    fa12.StorageFeeMultiplier;

                if (balanceInMtz - feeAmountInMtz - storageLimitInMtz - Xtz.MicroTezReserve <= 0)
                {
                    Log.Warning(
                        "Insufficient funds at {@address}. Balance: {@balance}, " +
                        "feeAmount: {@feeAmount}, storageLimit: {@storageLimit}.",
                        walletAddress.Address,
                        balanceInMtz,
                        feeAmountInMtz,
                        storageLimitInMtz);

                    continue;
                }

                var amountInTokens = requiredAmountInTokens > 0
                    ? AmountHelper.DustProofMin(balanceInTokens, requiredAmountInTokens, fa12.DigitsMultiplier, fa12.DustDigitsMultiplier)
                    : 0;

                if (amountInTokens == 0)
                    break;

                requiredAmountInTokens -= amountInTokens;

                var callingAddressPublicKey = (await Fa12Account.GetAddressAsync(walletAddress.Address)
                    .ConfigureAwait(false))
                    .PublicKeyBytes();

                var allowanceResult = await fa12Api
                    .TryGetTokenAllowanceAsync(
                        holderAddress: walletAddress.Address,
                        spenderAddress: fa12.SwapContractAddress,
                        callingAddress: walletAddress.Address,
                        publicKey: callingAddressPublicKey,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                if (allowanceResult.HasError)
                {
                    Log.Error("Error while getting token allowance for {@address} with code {@code} and description {@description}",
                        walletAddress.Address,
                        allowanceResult.Error.Code,
                        allowanceResult.Error.Description);

                    continue; // todo: maybe add approve 0
                }

                if (allowanceResult.Value > 0)
                {
                    transactions.Add(new TezosTransaction
                    {
                        Currency = fa12,
                        CreationTime = DateTime.UtcNow,
                        From = walletAddress.Address,
                        To = fa12.TokenContractAddress,
                        Fee = fa12.ApproveFee,
                        GasLimit = fa12.ApproveGasLimit,
                        StorageLimit = fa12.ApproveStorageLimit,
                        Params = ApproveParams(fa12.SwapContractAddress, 0),
                        UseDefaultFee = true,
                        Type = BlockchainTransactionType.TokenApprove
                    });
                }

                transactions.Add(new TezosTransaction
                {
                    Currency = fa12,
                    CreationTime = DateTime.UtcNow,
                    From = walletAddress.Address,
                    To = fa12.TokenContractAddress,
                    Fee = fa12.ApproveFee,
                    GasLimit = fa12.ApproveGasLimit,
                    StorageLimit = fa12.ApproveStorageLimit,
                    Params = ApproveParams(fa12.SwapContractAddress, amountInTokens.ToTokenDigits(fa12.DigitsMultiplier)),
                    UseDefaultFee = true,
                    Type = BlockchainTransactionType.TokenApprove
                });

                if (isInitTx)
                {
                    transactions.Add(new TezosTransaction
                    {
                        Currency = fa12,
                        CreationTime = DateTime.UtcNow,
                        From = walletAddress.Address,
                        To = fa12.SwapContractAddress,
                        Fee = feeAmountInMtz,
                        GasLimit = fa12.InitiateGasLimit,
                        StorageLimit = fa12.InitiateStorageLimit,
                        Params = InitParams(swap, fa12.TokenContractAddress, amountInTokens.ToTokenDigits(fa12.DigitsMultiplier), refundTimeStampUtcInSec, (long)rewardForRedeemInTokenDigits),
                        UseDefaultFee = true,
                        Type = BlockchainTransactionType.Output | BlockchainTransactionType.SwapPayment
                    });
                }
                //else
                //{
                //    transactions.Add(new TezosTransaction
                //    {
                //        Currency = Xtz,
                //        CreationTime = DateTime.UtcNow,
                //        From = walletAddress.Address,
                //        To = Xtz.SwapContractAddress,
                //        Fee = feeAmountInMtz,
                //        GasLimit = Xtz.AddGasLimit,
                //        StorageLimit = Xtz.AddStorageLimit,
                //        UseDefaultFee = true,
                //        Params = AddParams(swap),
                //        Type = BlockchainTransactionType.Output | BlockchainTransactionType.SwapPayment
                //    });
                //}

                if (isInitTx)
                    isInitTx = false;

                if (requiredAmountInTokens <= 0)
                    break;
            }

            if (requiredAmountInTokens > 0)
            {
                Log.Warning("Insufficient funds (left {@requredAmount}).", requiredAmountInTokens);
                return Enumerable.Empty<TezosTransaction>();
            }

            return transactions;
        }

        private async Task<bool> SignTransactionAsync(
            TezosTransaction tx,
            CancellationToken cancellationToken = default)
        {
            var walletAddress = await Fa12Account
                .GetAddressAsync(
                    address: tx.From,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            return await Fa12Account.Wallet
                .SignAsync(
                    tx: tx,
                    address: walletAddress,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }

        private async Task BroadcastTxAsync(
            Swap swap,
            TezosTransaction tx,
            CancellationToken cancellationToken = default,
            bool updateBalance = true,
            bool notifyIfUnconfirmed = true,
            bool notifyIfBalanceUpdated = true)
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
            await Fa12Account
                .UpsertTransactionAsync(
                    tx: tx,
                    updateBalance: updateBalance,
                    notifyIfUnconfirmed: notifyIfUnconfirmed,
                    notifyIfBalanceUpdated: notifyIfBalanceUpdated,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            var xtzTx = tx.Clone();
            xtzTx.Currency = Xtz;
            xtzTx.Amount = 0;
            xtzTx.Type = BlockchainTransactionType.Output | (tx.Type.HasFlag(BlockchainTransactionType.TokenApprove)
                ? BlockchainTransactionType.TokenCall
                : BlockchainTransactionType.SwapCall);

            await TezosAccount
                .UpsertTransactionAsync(
                    tx: xtzTx,
                    updateBalance: updateBalance,
                    notifyIfUnconfirmed: notifyIfUnconfirmed,
                    notifyIfBalanceUpdated: notifyIfBalanceUpdated,
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

                var tx = await Fa12Account
                    .GetTransactionByIdAsync<TezosTransaction>(txId)
                    .ConfigureAwait(false);

                if (tx != null && tx.IsConfirmed)
                    return true;
            }

            return false;
        }

        private JObject ApproveParams(
            string spender,
            decimal amount)
        {
            return JObject.Parse(@"{'entrypoint':'approve','value':{'prim':'Pair','args':[{'string':'" + spender + "'},{'int':'" + amount + "'}]}}");
        }

        private JObject InitParams(
            Swap swap,
            string tokenContractAddress,
            decimal tokenAmountInDigigts,
            long refundTimestamp,
            long redeemFeeAmount)
        {
            return JObject.Parse(@"{'entrypoint':'initiate','value':{'prim':'Pair','args':[{'prim':'Pair','args':[{'prim':'Pair','args':[{'bytes':'" + swap.SecretHash.ToHexString() + "'},{'string':'" + swap.PartyAddress + "'}]},{'prim':'Pair','args':[{'int':'" + redeemFeeAmount + "'},{'int':'" + refundTimestamp + "'}]}]},{'prim':'Pair','args':[{'string':'" + tokenContractAddress + "'},{'int':'" + tokenAmountInDigigts + "'}]}]}}");
        }

        //private JObject AddParams(Swap swap)
        //{
        //    return JObject.Parse(@"{'entrypoint':'add','value':{'prim':'Pair','args':[{'bytes':'" + swap.SecretHash.ToHexString() + "'},{'int':'" + tokenAmountInDigigts + "'}]}}");
        //}

        private JObject RedeemParams(Swap swap)
        {
            return JObject.Parse(@"{'entrypoint':'redeem','value':{'bytes':'" + swap.Secret.ToHexString() + "'}}");
        }

        private JObject RefundParams(Swap swap)
        {
            return JObject.Parse(@"{'entrypoint':'refund','value':{'bytes':'" + swap.SecretHash.ToHexString() + "'}}");
        }

        #endregion Helpers
    }
}