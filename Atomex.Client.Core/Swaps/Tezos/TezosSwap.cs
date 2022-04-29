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
        public const int MaxRedeemCheckAttempts = 10;
        public const int MaxRefundCheckAttempts = 10;
        public const int RedeemCheckAttemptIntervalInSec = 5;
        public const int RefundCheckAttemptIntervalInSec = 5;
        public static TimeSpan InitiationTimeout = TimeSpan.FromMinutes(10);
        public static TimeSpan InitiationCheckInterval = TimeSpan.FromSeconds(15);

        private TezosConfig XtzConfig => Currencies.Get<TezosConfig>(Currency);
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
            Log.Error("DEBUG: Tezos before pay relevance check for swap {@swapId}", swap.Id);

            if (!await CheckPayRelevanceAsync(swap, cancellationToken))
                return;

            var lockTimeInSeconds = swap.IsInitiator
                ? DefaultInitiatorLockTimeInSeconds
                : DefaultAcceptorLockTimeInSeconds;

            var paymentTxs = (await CreatePaymentTxsAsync(swap, lockTimeInSeconds, cancellationToken)
                .ConfigureAwait(false))
                .ToList();

            Log.Error("DEBUG: Tezos payment tx created for swap {@swapId}", swap.Id);

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
                    try
                    {
                        await _account.AddressLocker
                            .LockAsync(paymentTx.From, cancellationToken)
                            .ConfigureAwait(false);

                        Log.Error("DEBUG: Tezos payment lock for swap {@swapId}", swap.Id);

                        // temporary fix: check operation sequence
                        await TezosOperationsSequencer
                            .WaitAsync(paymentTx.From, _account, cancellationToken)
                            .ConfigureAwait(false);

                        Log.Error("DEBUG: Tezos payment TezosOperationsSequencer wait success for swap {@swapId}", swap.Id);

                        var address = await _account
                            .GetAddressAsync(paymentTx.From, cancellationToken)
                            .ConfigureAwait(false);

                        using var securePublicKey = _account.Wallet
                            .GetPublicKey(XtzConfig, address.KeyIndex, address.KeyType);

                        // fill operation
                        var (fillResult, isRunSuccess, hasReveal) = await paymentTx
                            .FillOperationsAsync(
                                securePublicKey: securePublicKey,
                                tezosConfig: XtzConfig,
                                headOffset: TezosConfig.HeadOffset,
                                cancellationToken: cancellationToken)
                            .ConfigureAwait(false);

                        Log.Error("DEBUG: Tezos payment FillOperationsAsync success for swap {@swapId}", swap.Id);

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

                        Log.Error("DEBUG: Tezos payment Sign success for swap {@swapId}", swap.Id);

                        await BroadcastTxAsync(swap, paymentTx, cancellationToken)
                            .ConfigureAwait(false);
                    }
                    catch
                    {
                        throw;
                    }
                    finally
                    {
                        _account.AddressLocker.Unlock(paymentTx.From);
                    }

                    if (isInitiateTx)
                    {
                        swap.PaymentTx = paymentTx;
                        swap.StateFlags |= SwapStateFlags.IsPaymentBroadcast;

                        await UpdateSwapAsync(swap, SwapStateFlags.IsPaymentBroadcast, cancellationToken)
                            .ConfigureAwait(false);

                        isInitiateTx = false;

                        // check initiate payment tx confirmation
                        var isInitiated =
                            await WaitPaymentConfirmationAsync(paymentTx.Id, InitiationTimeout, cancellationToken)
                                .ConfigureAwait(false);
                        
                        if (!isInitiated)
                        {
                            Log.Error("Initiation payment tx not confirmed after timeout {@timeout}",
                                InitiationTimeout.Minutes);
                            return;
                        }
                    }
                }
                
                swap.StateFlags |= SwapStateFlags.IsPaymentConfirmed;
                await UpdateSwapAsync(swap, SwapStateFlags.IsPaymentConfirmed, cancellationToken)
                    .ConfigureAwait(false);
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

            _ = TezosSwapInitiatedHelper.StartSwapInitiatedControlAsync(
                    swap: swap,
                    currency: XtzConfig,
                    refundTimeStamp: refundTimeUtcInSec,
                    interval: ConfirmationCheckInterval,
                    initiatedHandler: initiatedHandler,
                    canceledHandler: SwapCanceledHandler,
                    cancellationToken: cancellationToken);

            return Task.CompletedTask;
        }

        public override async Task RedeemAsync(
            Swap swap,
            CancellationToken cancellationToken = default)
        {
            var xtzConfig = XtzConfig;

            var secretResult = await TezosSwapRedeemedHelper
                .IsRedeemedAsync(
                    swap: swap,
                    currency: xtzConfig,
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
                _ = TrackTransactionConfirmationAsync(
                    swap: swap,
                    currency: xtzConfig,
                    dataRepository: _account.DataRepository,
                    txId: swap.RedeemTx.Id,
                    confirmationHandler: RedeemConfirmedEventHandler,
                    cancellationToken: cancellationToken);

                return;
            }

            // check already refunded by initiator
            if (swap.IsAcceptor &&
                swap.TimeStamp.ToUniversalTime().AddSeconds(DefaultInitiatorLockTimeInSeconds) < DateTime.UtcNow)
            {
                var isRefundedByParty = await TezosSwapRefundedHelper
                    .IsRefundedAsync(swap, xtzConfig, cancellationToken)
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
                Currency     = xtzConfig.Name,
                CreationTime = DateTime.UtcNow,
                From         = walletAddress.Address,
                To           = xtzConfig.SwapContractAddress,
                Amount       = 0,
                Fee          = xtzConfig.RedeemFee + xtzConfig.RevealFee,
                GasLimit     = xtzConfig.RedeemGasLimit,
                StorageLimit = xtzConfig.RedeemStorageLimit,
                Params       = CreateRedeemParams(swap),
                Type         = BlockchainTransactionType.Output | BlockchainTransactionType.SwapRedeem,

                UseRun              = true,
                UseSafeStorageLimit = true,
                UseOfflineCounter   = true
            };

            try
            {
                await _account.AddressLocker
                    .LockAsync(redeemTx.From, cancellationToken)
                    .ConfigureAwait(false);

                // temporary fix: check operation sequence
                await TezosOperationsSequencer
                    .WaitAsync(redeemTx.From, _account, cancellationToken)
                    .ConfigureAwait(false);

                using var securePublicKey = _account.Wallet
                    .GetPublicKey(xtzConfig, walletAddress.KeyIndex, walletAddress.KeyType);

                // fill operation
                var (fillResult, isRunSuccess, hasReveal) = await redeemTx
                    .FillOperationsAsync(
                        securePublicKey: securePublicKey,
                        tezosConfig: xtzConfig,
                        headOffset: TezosConfig.HeadOffset,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

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
            }
            catch
            {
                throw;
            }
            finally
            {
                _account.AddressLocker.Unlock(redeemTx.From);
            }

            swap.RedeemTx = redeemTx;
            swap.StateFlags |= SwapStateFlags.IsRedeemBroadcast;

            await UpdateSwapAsync(swap, SwapStateFlags.IsRedeemBroadcast, cancellationToken)
                .ConfigureAwait(false);

            _ = TrackTransactionConfirmationAsync(
                swap: swap,
                currency: xtzConfig,
                dataRepository: _account.DataRepository,
                txId: redeemTx.Id,
                confirmationHandler: RedeemConfirmedEventHandler,
                cancellationToken: cancellationToken);
        }

        public override async Task RedeemForPartyAsync(
            Swap swap,
            CancellationToken cancellationToken = default)
        {
            if (swap.IsInitiator)
            {
                var partyRedeemDeadline = swap.TimeStamp.ToUniversalTime().AddSeconds(DefaultAcceptorLockTimeInSeconds) - PartyRedeemTimeReserve;

                if (DateTime.UtcNow > partyRedeemDeadline)
                {
                    Log.Error("Party redeem deadline reached for swap {@swap}", swap.Id);
                    return;
                }
            }

            Log.Debug("Create redeem for acceptor for swap {@swapId}", swap.Id);

            var xtzConfig = XtzConfig;

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
                Currency     = xtzConfig.Name,
                CreationTime = DateTime.UtcNow,
                From         = walletAddress.Address,
                To           = xtzConfig.SwapContractAddress,
                Amount       = 0,
                Fee          = xtzConfig.RedeemFee + xtzConfig.RevealFee,
                GasLimit     = xtzConfig.RedeemGasLimit,
                StorageLimit = xtzConfig.RedeemStorageLimit,
                Params       = CreateRedeemParams(swap),
                Type         = BlockchainTransactionType.Output | BlockchainTransactionType.SwapRedeem,

                UseRun              = true,
                UseSafeStorageLimit = true,
                UseOfflineCounter   = true
            };

            using var addressLock = await _account.AddressLocker
                .GetLockAsync(redeemTx.From, cancellationToken)
                .ConfigureAwait(false);

            // temporary fix: check operation sequence
            await TezosOperationsSequencer
                .WaitAsync(redeemTx.From, _account, cancellationToken)
                .ConfigureAwait(false);

            using var securePublicKey = _account.Wallet
                .GetPublicKey(xtzConfig, walletAddress.KeyIndex, walletAddress.KeyType);

            // fill operation
            var (fillResult, isRunSuccess, hasReveal) = await redeemTx
                .FillOperationsAsync(
                    securePublicKey: securePublicKey,
                    tezosConfig: xtzConfig,
                    headOffset: TezosConfig.HeadOffset,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

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
            var xtzConfig = XtzConfig;

            if (swap.StateFlags.HasFlag(SwapStateFlags.IsRefundBroadcast) &&
                swap.RefundTx != null &&
                swap.RefundTx.CreationTime != null &&
                swap.RefundTx.CreationTime.Value.ToUniversalTime() + TimeSpan.FromMinutes(5) > DateTime.UtcNow)
            {
                _ = TrackTransactionConfirmationAsync(
                    swap: swap,
                    currency: xtzConfig,
                    dataRepository: _account.DataRepository,
                    txId: swap.RefundTx.Id,
                    confirmationHandler: RefundConfirmedEventHandler,
                    cancellationToken: cancellationToken);

                return;
            }

            var lockTimeInSeconds = swap.IsInitiator
                ? DefaultInitiatorLockTimeInSeconds
                : DefaultAcceptorLockTimeInSeconds;

            var lockTime = swap.TimeStamp.ToUniversalTime() + TimeSpan.FromSeconds(lockTimeInSeconds);

            await RefundTimeDelayAsync(lockTime, cancellationToken)
                .ConfigureAwait(false);

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

            var refundTx = new TezosTransaction
            {
                Currency     = xtzConfig.Name,
                CreationTime = DateTime.UtcNow,
                From         = walletAddress.Address,
                To           = xtzConfig.SwapContractAddress,
                Fee          = xtzConfig.RefundFee + xtzConfig.RevealFee,
                GasLimit     = xtzConfig.RefundGasLimit,
                StorageLimit = xtzConfig.RefundStorageLimit,
                Params       = CreateRefundParams(swap),
                Type         = BlockchainTransactionType.Output | BlockchainTransactionType.SwapRefund,

                UseRun              = true,
                UseSafeStorageLimit = true,
                UseOfflineCounter   = true
            };

            try
            {
                await _account.AddressLocker
                    .LockAsync(refundTx.From, cancellationToken)
                    .ConfigureAwait(false);

                // temporary fix: check operation sequence
                await TezosOperationsSequencer
                    .WaitAsync(refundTx.From, _account, cancellationToken)
                    .ConfigureAwait(false);

                using var securePublicKey = _account.Wallet
                    .GetPublicKey(xtzConfig, walletAddress.KeyIndex, walletAddress.KeyType);

                // fill operation
                var (fillResult, isRunSuccess, hasReveal) = await refundTx
                    .FillOperationsAsync(
                        securePublicKey: securePublicKey,
                        tezosConfig: xtzConfig,
                        headOffset: TezosConfig.HeadOffset,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

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
            }
            catch
            {
                throw;
            }
            finally
            {
                _account.AddressLocker.Unlock(refundTx.From);
            }

            swap.RefundTx = refundTx;
            swap.StateFlags |= SwapStateFlags.IsRefundBroadcast;

            await UpdateSwapAsync(swap, SwapStateFlags.IsRefundBroadcast, cancellationToken)
                .ConfigureAwait(false);

            _ = TrackTransactionConfirmationAsync(
                swap: swap,
                currency: xtzConfig,
                dataRepository: _account.DataRepository,
                txId: refundTx.Id,
                confirmationHandler: RefundConfirmedEventHandler,
                cancellationToken: cancellationToken);
        }

        public override Task StartWaitForRedeemAsync(
            Swap swap,
            CancellationToken cancellationToken = default)
        {
            var lockTimeInSeconds = swap.IsInitiator
                ? DefaultInitiatorLockTimeInSeconds
                : DefaultAcceptorLockTimeInSeconds;

            // start redeem control async
            _ = TezosSwapRedeemedHelper.StartSwapRedeemedControlAsync(
                swap: swap,
                currency: XtzConfig,
                refundTimeUtc: swap.TimeStamp.ToUniversalTime().AddSeconds(lockTimeInSeconds),
                interval: TimeSpan.FromSeconds(30),
                cancelOnlyIfRefundTimeReached: true,
                redeemedHandler: RedeemCompletedEventHandler,
                canceledHandler: RedeemCanceledEventHandler,
                cancellationToken: cancellationToken);

            return Task.CompletedTask;
        }

        public override Task StartWaitForRedeemBySomeoneAsync(
            Swap swap,
            CancellationToken cancellationToken = default)
        {
            Log.Debug("Wait redeem for swap {@swapId}", swap.Id);

            // start redeem control async
            _ = TezosSwapRedeemedHelper.StartSwapRedeemedControlAsync(
                swap: swap,
                currency: XtzConfig,
                refundTimeUtc: swap.TimeStamp.ToUniversalTime().AddSeconds(DefaultAcceptorLockTimeInSeconds),
                interval: TimeSpan.FromSeconds(30),
                cancelOnlyIfRefundTimeReached: true,
                redeemedHandler: RedeemBySomeoneCompletedEventHandler,
                canceledHandler: RedeemBySomeoneCanceledEventHandler,
                cancellationToken: cancellationToken);

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
                        currency: XtzConfig,
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
                _ = AddressHelper.UpdateAddressBalanceAsync<TezosWalletScanner, TezosAccount>(
                    account: _account,
                    address: swap.ToAddress,
                    cancellationToken: cancellationToken);
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
            var xtzConfig = XtzConfig;

            Log.Debug("Create payment transactions for swap {@swapId}", swap.Id);

            var requiredAmountInMtz = AmountHelper
                .QtyToAmount(swap.Side, swap.Qty, swap.Price, xtzConfig.DigitsMultiplier)
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
                    ? xtzConfig.InitiateFee + (isRevealed ? 0 : xtzConfig.RevealFee)
                    : xtzConfig.AddFee + (isRevealed ? 0 : xtzConfig.RevealFee);

                var storageLimitInMtz = isInitTx
                    ? xtzConfig.InitiateStorageLimit * xtzConfig.StorageFeeMultiplier
                    : xtzConfig.AddStorageLimit * xtzConfig.StorageFeeMultiplier;

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
                        Currency     = xtzConfig.Name,
                        CreationTime = DateTime.UtcNow,
                        From         = walletAddress.Address,
                        To           = xtzConfig.SwapContractAddress,
                        Amount       = Math.Round(amountInMtz, 0),
                        Fee          = feeAmountInMtz,
                        GasLimit     = xtzConfig.InitiateGasLimit,
                        StorageLimit = xtzConfig.InitiateStorageLimit,
                        Params       = CreateInitParams(swap, refundTimeStampUtcInSec, (long)rewardForRedeemInMtz),
                        Type         = BlockchainTransactionType.Output | BlockchainTransactionType.SwapPayment,

                        UseRun              = true,
                        UseSafeStorageLimit = true,
                        UseOfflineCounter   = true
                    });
                }
                else
                {
                    transactions.Add(new TezosTransaction
                    {
                        Currency     = xtzConfig.Name,
                        CreationTime = DateTime.UtcNow,
                        From         = walletAddress.Address,
                        To           = xtzConfig.SwapContractAddress,
                        Amount       = Math.Round(amountInMtz, 0),
                        Fee          = feeAmountInMtz,
                        GasLimit     = xtzConfig.AddGasLimit,
                        StorageLimit = xtzConfig.AddStorageLimit,
                        Params       = CreateAddParams(swap),
                        Type         = BlockchainTransactionType.Output | BlockchainTransactionType.SwapPayment,

                        UseRun              = true,
                        UseSafeStorageLimit = true,
                        UseOfflineCounter   = true
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
                    currency: XtzConfig,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }

        private async Task BroadcastTxAsync(
            Swap swap,
            TezosTransaction tx,
            CancellationToken cancellationToken = default)
        {
            var broadcastResult = await XtzConfig.BlockchainApi
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

                var tx = await _account
                    .DataRepository
                    .GetTransactionByIdAsync(XtzConfig.Name, txId, XtzConfig.TransactionType)
                    .ConfigureAwait(false);

                if (tx is not { IsConfirmed: true }) continue;

                return tx.IsConfirmed;
            }

            return false;
        }

        private JObject CreateInitParams(
            Swap swap,
            long refundTimestamp,
            long redeemFeeAmount)
        {
            return JObject.Parse(@"{'entrypoint':'default','value':{'prim':'Left','args':[{'prim':'Left','args':[{'prim':'Pair','args':[{'string':'" + swap.PartyAddress + "'},{'prim':'Pair','args':[{'prim':'Pair','args':[{'bytes':'" + swap.SecretHash.ToHexString() + "'},{'int':'" + refundTimestamp + "'}]},{'int':'" + redeemFeeAmount + "'}]}]}]}]}}");
        }

        private JObject CreateAddParams(Swap swap)
        {
            return JObject.Parse(@"{'entrypoint':'default','value':{'prim':'Left','args':[{'prim':'Right','args':[{'bytes':'" + swap.SecretHash.ToHexString() + "'}]}]}}");
        }

        private JObject CreateRedeemParams(Swap swap)
        {
            return JObject.Parse(@"{'entrypoint':'default','value':{'prim':'Right','args':[{'prim':'Left','args':[{'bytes':'" + swap.Secret.ToHexString() + "'}]}]}}");
        }

        private JObject CreateRefundParams(Swap swap)
        {
            return JObject.Parse(@"{'entrypoint':'default','value':{'prim':'Right','args':[{'prim':'Right','args':[{'bytes':'" + swap.SecretHash.ToHexString() + "'}]}]}}");
        }

        #endregion Helpers
    }
}