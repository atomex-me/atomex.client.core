using System;
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

        private TezosConfig_OLD XtzConfig => Currencies.Get<TezosConfig_OLD>(Currency);
        protected readonly TezosAccount_OLD _account;

        public TezosSwap(
            TezosAccount_OLD account,
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

            var paymentTx = await CreatePaymentTxAsync(swap, lockTimeInSeconds, cancellationToken)
                .ConfigureAwait(false);

            if (paymentTx == null)
            {
                Log.Error("Can't create payment transaction");
                return;
            }

            try
            {
                try
                {
                    await _account.AddressLocker
                        .LockAsync(paymentTx.From, cancellationToken)
                        .ConfigureAwait(false);

                    // temporary fix: check operation sequence
                    await TezosOperationsSequencer_OLD
                        .WaitAsync(paymentTx.From, _account, cancellationToken)
                        .ConfigureAwait(false);

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
                            headOffset: TezosConfig_OLD.HeadOffset,
                            cancellationToken: cancellationToken)
                        .ConfigureAwait(false);

                    var signResult = await SignTransactionAsync(paymentTx, cancellationToken)
                        .ConfigureAwait(false);

                    if (!signResult)
                    {
                        Log.Error("Transaction signing error");
                        return;
                    }

                    swap.PaymentTx = paymentTx;
                    swap.StateFlags |= SwapStateFlags.IsPaymentSigned;

                    await UpdateSwapAsync(swap, SwapStateFlags.IsPaymentSigned, cancellationToken)
                        .ConfigureAwait(false);

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

                swap.PaymentTx = paymentTx;
                swap.StateFlags |= SwapStateFlags.IsPaymentBroadcast;

                await UpdateSwapAsync(swap, SwapStateFlags.IsPaymentBroadcast, cancellationToken)
                    .ConfigureAwait(false);

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

            var walletAddress = await _account
                .GetAddressAsync(swap.RedeemFromAddress, cancellationToken)
                .ConfigureAwait(false);

            if (walletAddress == null)
            {
                Log.Error("Can't get address {@address} for redeem from local db", swap.RedeemFromAddress);
                return;
            }

            var feeAmountInMtz = xtzConfig.RedeemFee + xtzConfig.RevealFee;
            var storageLimitInMtz = xtzConfig.RedeemStorageLimit * xtzConfig.StorageFeeMultiplier;

            if (walletAddress.Balance.ToMicroTez() < feeAmountInMtz + storageLimitInMtz)
            {
                Log.Error("Insufficient funds for redeem");
                return;
            }

            var redeemTx = new TezosTransaction_OLD
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
                await TezosOperationsSequencer_OLD
                    .WaitAsync(redeemTx.From, _account, cancellationToken)
                    .ConfigureAwait(false);

                using var securePublicKey = _account.Wallet
                    .GetPublicKey(xtzConfig, walletAddress.KeyIndex, walletAddress.KeyType);

                // fill operation
                var (fillResult, isRunSuccess, hasReveal) = await redeemTx
                    .FillOperationsAsync(
                        securePublicKey: securePublicKey,
                        tezosConfig: xtzConfig,
                        headOffset: TezosConfig_OLD.HeadOffset,
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

            var walletAddress = await _account
                .GetAddressAsync(swap.FromAddress, cancellationToken)
                .ConfigureAwait(false);

            if (walletAddress == null)
            {
                Log.Error("Can't get address {@address} for redeem for party from local db", swap.FromAddress);
                return;
            }

            var feeAmountInMtz = xtzConfig.RedeemFee + xtzConfig.RevealFee;
            var storageLimitInMtz = xtzConfig.RedeemStorageLimit * xtzConfig.StorageFeeMultiplier;

            if (walletAddress.Balance.ToMicroTez() < feeAmountInMtz + storageLimitInMtz)
            {
                Log.Error("Insufficient funds for redeem for party");
            }

            var redeemForPartyTx = new TezosTransaction_OLD
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
                using var addressLock = await _account.AddressLocker
                    .GetLockAsync(redeemForPartyTx.From, cancellationToken)
                    .ConfigureAwait(false);

                // temporary fix: check operation sequence
                await TezosOperationsSequencer_OLD
                    .WaitAsync(redeemForPartyTx.From, _account, cancellationToken)
                    .ConfigureAwait(false);

                using var securePublicKey = _account.Wallet
                    .GetPublicKey(xtzConfig, walletAddress.KeyIndex, walletAddress.KeyType);

                // fill operation
                var (fillResult, isRunSuccess, hasReveal) = await redeemForPartyTx
                    .FillOperationsAsync(
                        securePublicKey: securePublicKey,
                        tezosConfig: xtzConfig,
                        headOffset: TezosConfig_OLD.HeadOffset,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                var signResult = await SignTransactionAsync(redeemForPartyTx, cancellationToken)
                    .ConfigureAwait(false);

                if (!signResult)
                {
                    Log.Error("Transaction signing error");
                    return;
                }

                await BroadcastTxAsync(swap, redeemForPartyTx, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch
            {
                throw;
            }
            finally
            {
                _account.AddressLocker.Unlock(redeemForPartyTx.From);
            }
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

            var walletAddress = await _account
                .GetAddressAsync(swap.FromAddress, cancellationToken)
                .ConfigureAwait(false);

            if (walletAddress == null)
            {
                Log.Error("Can't get address {@address} for refund from local db", swap.FromAddress);
                return;
            }

            var feeAmountInMtz = xtzConfig.RefundFee + xtzConfig.RevealFee;
            var storageLimitInMtz = xtzConfig.RefundStorageLimit * xtzConfig.StorageFeeMultiplier;

            if (walletAddress.Balance.ToMicroTez() < feeAmountInMtz + storageLimitInMtz)
            {
                Log.Error("Insufficient funds for refund");
            }

            var refundTx = new TezosTransaction_OLD
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
                await TezosOperationsSequencer_OLD
                    .WaitAsync(refundTx.From, _account, cancellationToken)
                    .ConfigureAwait(false);

                using var securePublicKey = _account.Wallet
                    .GetPublicKey(xtzConfig, walletAddress.KeyIndex, walletAddress.KeyType);

                // fill operation
                var (fillResult, isRunSuccess, hasReveal) = await refundTx
                    .FillOperationsAsync(
                        securePublicKey: securePublicKey,
                        tezosConfig: xtzConfig,
                        headOffset: TezosConfig_OLD.HeadOffset,
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

        public override async Task<Result<IBlockchainTransaction_OLD>> TryToFindPaymentAsync(
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
                _ = AddressHelper.UpdateAddressBalanceAsync<TezosWalletScanner_OLD, TezosAccount_OLD>(
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

        protected virtual async Task<TezosTransaction_OLD> CreatePaymentTxAsync(
            Swap swap,
            int lockTimeSeconds,
            CancellationToken cancellationToken = default)
        {
            var xtzConfig = XtzConfig;

            Log.Debug("Create {@currency} payment transaction from address {@address} for swap {@swapId}",
                Currency,
                swap.FromAddress,
                swap.Id);

            var requiredAmountInMtz = AmountHelper
                .QtyToSellAmount(swap.Side, swap.Qty, swap.Price, xtzConfig.DigitsMultiplier)
                .ToMicroTez();

            // maker network fee
            if (swap.MakerNetworkFee > 0)
            {
                var makerNetworkFeeInMtz = swap.MakerNetworkFee.ToMicroTez();

                if (makerNetworkFeeInMtz < requiredAmountInMtz) // network fee size check
                    requiredAmountInMtz += makerNetworkFeeInMtz;
            }

            var refundTimeStampUtcInSec = new DateTimeOffset(swap.TimeStamp.ToUniversalTime().AddSeconds(lockTimeSeconds)).ToUnixTimeSeconds();

            var rewardForRedeemInMtz = swap.IsInitiator
                ? swap.PartyRewardForRedeem.ToMicroTez()
                : 0;

            var walletAddress = await _account
                .GetAddressAsync(swap.FromAddress, cancellationToken)
                .ConfigureAwait(false);

            Log.Debug("Available balance: {@balance}", walletAddress.Balance);

            var balanceInMtz = walletAddress.Balance.ToMicroTez();

            var isRevealed = await _account
                .IsRevealedSourceAsync(walletAddress.Address, cancellationToken)
                .ConfigureAwait(false);

            var feeAmountInMtz = xtzConfig.InitiateFee + (isRevealed ? 0 : xtzConfig.RevealFee);
            var storageLimitInMtz = xtzConfig.InitiateStorageLimit * xtzConfig.StorageFeeMultiplier;

            if (balanceInMtz < feeAmountInMtz + storageLimitInMtz + requiredAmountInMtz)
            {
                Log.Error(
                    "Insufficient funds at {@address}. Balance: {@balance}, required: {@required}, " +
                    "feeAmount: {@feeAmount}, storageLimit: {@storageLimit}, missing: {@result}.",
                    walletAddress.Address,
                    balanceInMtz,
                    requiredAmountInMtz,
                    feeAmountInMtz,
                    storageLimitInMtz,
                    balanceInMtz - feeAmountInMtz - storageLimitInMtz - requiredAmountInMtz);

                return null;
            }

            return new TezosTransaction_OLD
            {
                Currency     = xtzConfig.Name,
                CreationTime = DateTime.UtcNow,
                From         = walletAddress.Address,
                To           = xtzConfig.SwapContractAddress,
                Amount       = Math.Round(requiredAmountInMtz, 0),
                Fee          = feeAmountInMtz,
                GasLimit     = xtzConfig.InitiateGasLimit,
                StorageLimit = xtzConfig.InitiateStorageLimit,
                Params       = CreateInitParams(swap, refundTimeStampUtcInSec, (long)rewardForRedeemInMtz),
                Type         = BlockchainTransactionType.Output | BlockchainTransactionType.SwapPayment,

                UseRun              = true,
                UseSafeStorageLimit = true,
                UseOfflineCounter   = true
            };
        }

        private async Task<bool> SignTransactionAsync(
            TezosTransaction_OLD tx,
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
            TezosTransaction_OLD tx,
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