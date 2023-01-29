using System;
using System.Threading;
using System.Threading.Tasks;

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
            if (!await CheckPayRelevanceAsync(swap, cancellationToken))
                return;

            var lockTimeInSeconds = swap.IsInitiator
                ? DefaultInitiatorLockTimeInSeconds
                : DefaultAcceptorLockTimeInSeconds;

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

            var refundTimeStampUtcInSec = new DateTimeOffset(swap.TimeStamp.ToUniversalTime().AddSeconds(lockTimeInSeconds))
                .ToUnixTimeSeconds();

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

                return;
            }

            var (result, error) = await _account
                .SendTransactionAsync(
                    from: walletAddress.Address,
                    to: xtzConfig.SwapContractAddress,
                    amount: requiredAmountInMtz,
                    fee: Fee.FromNetwork((long)feeAmountInMtz),
                    gasLimit: GasLimit.FromValue((int)xtzConfig.InitiateGasLimit),
                    storageLimit: StorageLimit.FromValue((int)xtzConfig.InitiateStorageLimit),
                    entrypoint: "initiate",
                    parameters: GetInitiateParameters(swap, refundTimeStampUtcInSec, rewardForRedeemInMtz),
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (error != null)
            {
                Log.Error($"Error while send Tezos initiate transaction. Code: {error.Value.Code}. Message: {error.Value.Message}");
                return;
            }

            if (result.Error != null)
            {
                Log.Error($"Error while send Tezos initiate transaction. Code: {error.Value.Code}. Message: {error.Value.Message}");
                return;
            }

            swap.PaymentTxId = result.OperationId;
            swap.StateFlags |= SwapStateFlags.IsPaymentSigned | SwapStateFlags.IsPaymentBroadcast;

            await UpdateSwapAsync(swap, SwapStateFlags.IsPaymentSigned | SwapStateFlags.IsPaymentBroadcast, cancellationToken)
                .ConfigureAwait(false);

            // check initiate payment tx confirmation
            var isInitiated = await WaitPaymentConfirmationAsync(
                    txId: result.OperationId,
                    timeout: InitiationTimeout,
                    cancellationToken: cancellationToken)
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

        public override Task StartPartyPaymentControlAsync(
            Swap swap,
            CancellationToken cancellationToken = default)
        {
            Log.Debug("Start party {@currency} payment control for swap {@swap}", Currency, swap.Id);

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

            var (secret, isRedeemedError) = await TezosSwapRedeemedHelper
                .IsRedeemedAsync(
                    swap: swap,
                    currency: xtzConfig,
                    attempts: MaxRedeemCheckAttempts,
                    attemptIntervalInSec: RedeemCheckAttemptIntervalInSec,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (isRedeemedError == null && secret != null)
            {
                await RedeemConfirmedEventHandler(swap, cancellationToken)
                    .ConfigureAwait(false);

                return;
            }

            if (swap.StateFlags.HasFlag(SwapStateFlags.IsRedeemBroadcast) &&
                swap.LastRedeemTryTimeStamp + TimeSpan.FromMinutes(5) > DateTime.UtcNow)
            {
                // redeem already broadcast
                _ = TrackTransactionConfirmationAsync<TezosOperation>(
                    swap: swap,
                    localStorage: _account.LocalStorage,
                    txId: swap.RedeemTxId,
                    confirmationHandler: RedeemConfirmedEventHandler,
                    cancellationToken: cancellationToken);

                return;
            }

            // check already refunded by initiator
            if (swap.IsAcceptor &&
                swap.TimeStamp.ToUniversalTime().AddSeconds(DefaultInitiatorLockTimeInSeconds) < DateTime.UtcNow)
            {
                var (isRefunded, isRefundedError) = await TezosSwapRefundedHelper
                    .IsRefundedAsync(swap, xtzConfig, cancellationToken)
                    .ConfigureAwait(false);

                if (isRefundedError == null && isRefunded)
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

            var (result, error) = await _account
                .SendTransactionAsync(
                    from: walletAddress.Address,
                    to: xtzConfig.SwapContractAddress,
                    amount: 0,
                    fee: Fee.FromNetwork((long)(xtzConfig.RedeemFee + xtzConfig.RevealFee)),
                    gasLimit: GasLimit.FromValue((int)xtzConfig.RedeemGasLimit),
                    storageLimit: StorageLimit.FromValue((int)xtzConfig.RedeemStorageLimit),
                    entrypoint: "redeem",
                    parameters: GetRedeemParameters(swap),
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (error != null)
            {
                Log.Error($"Error while send Tezos redeem transaction. Code: {error.Value.Code}. Message: {error.Value.Message}");
                return;
            }

            if (result.Error != null)
            {
                Log.Error($"Error while send Tezos redeem transaction. Code: {error.Value.Code}. Message: {error.Value.Message}");
                return;
            }

            swap.RedeemTxId = result.OperationId;
            swap.LastRedeemTryTimeStamp = DateTime.UtcNow;
            swap.StateFlags |= SwapStateFlags.IsRedeemSigned | SwapStateFlags.IsRedeemBroadcast;

            await UpdateSwapAsync(swap, SwapStateFlags.IsRedeemSigned | SwapStateFlags.IsRedeemBroadcast, cancellationToken)
                .ConfigureAwait(false);

            _ = TrackTransactionConfirmationAsync<TezosOperation>(
                swap: swap,
                localStorage: _account.LocalStorage,
                txId: result.OperationId,
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

            var (result, error) = await _account
                .SendTransactionAsync(
                    from: walletAddress.Address,
                    to: xtzConfig.SwapContractAddress,
                    amount: 0,
                    fee: Fee.FromNetwork((long)(xtzConfig.RedeemFee + xtzConfig.RevealFee)),
                    gasLimit: GasLimit.FromValue((int)xtzConfig.RedeemGasLimit),
                    storageLimit: StorageLimit.FromValue((int)xtzConfig.RedeemStorageLimit),
                    entrypoint: "redeem",
                    parameters: GetRedeemParameters(swap),
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (error != null)
            {
                Log.Error($"Error while send Tezos redeem for party transaction. Code: {error.Value.Code}. Message: {error.Value.Message}");
                return;
            }

            if (result.Error != null)
            {
                Log.Error($"Error while send Tezos redeem for party transaction. Code: {error.Value.Code}. Message: {error.Value.Message}");
                return;
            }
        }

        public override async Task RefundAsync(
            Swap swap,
            CancellationToken cancellationToken = default)
        {
            var xtzConfig = XtzConfig;

            if (swap.StateFlags.HasFlag(SwapStateFlags.IsRefundBroadcast) &&
                swap.LastRefundTryTimeStamp + TimeSpan.FromMinutes(5) > DateTime.UtcNow)
            {
                _ = TrackTransactionConfirmationAsync<TezosOperation>(
                    swap: swap,
                    localStorage: _account.LocalStorage,
                    txId: swap.RefundTxId,
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

            var (result, error) = await _account
                .SendTransactionAsync(
                    from: walletAddress.Address,
                    to: xtzConfig.SwapContractAddress,
                    amount: 0,
                    fee: Fee.FromNetwork((long)(xtzConfig.RefundFee + xtzConfig.RevealFee)),
                    gasLimit: GasLimit.FromValue((int)xtzConfig.RefundGasLimit),
                    storageLimit: StorageLimit.FromValue((int)xtzConfig.RefundStorageLimit),
                    entrypoint: "refund",
                    parameters: GetRefundParameters(swap),
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (error != null)
            {
                Log.Error($"Error while send Tezos refund transaction. Code: {error.Value.Code}. Message: {error.Value.Message}");
                return;
            }

            if (result.Error != null)
            {
                Log.Error($"Error while send Tezos refund transaction. Code: {error.Value.Code}. Message: {error.Value.Message}");
                return;
            }

            swap.RefundTxId = result.OperationId;
            swap.LastRefundTryTimeStamp = DateTime.UtcNow;
            swap.StateFlags |= SwapStateFlags.IsRefundSigned | SwapStateFlags.IsRefundBroadcast;

            await UpdateSwapAsync(swap, SwapStateFlags.IsRefundSigned | SwapStateFlags.IsRefundBroadcast, cancellationToken)
                .ConfigureAwait(false);

            _ = TrackTransactionConfirmationAsync<TezosOperation>(
                swap: swap,
                localStorage: _account.LocalStorage,
                txId: result.OperationId,
                confirmationHandler: RefundConfirmedEventHandler,
                cancellationToken: cancellationToken);
        }

        public override Task StartWaitingForRedeemAsync(
            Swap swap,
            CancellationToken cancellationToken = default)
        {
            Log.Debug("Start waiting for {@currency} redeem for swap {@swap}", Currency, swap.Id);

            var lockTimeInSeconds = swap.IsInitiator
                ? DefaultInitiatorLockTimeInSeconds
                : DefaultAcceptorLockTimeInSeconds;

            // start redeem control async
            _ = TezosSwapRedeemedHelper.StartSwapRedeemedControlAsync(
                swap: swap,
                currency: XtzConfig,
                refundTimeUtc: swap.TimeStamp.ToUniversalTime().AddSeconds(lockTimeInSeconds),
                interval: TimeSpan.FromSeconds(30),
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
                redeemedHandler: RedeemBySomeoneCompletedEventHandler,
                canceledHandler: RedeemBySomeoneCanceledEventHandler,
                cancellationToken: cancellationToken);

            return Task.CompletedTask;
        }

        public override async Task<Result<ITransaction>> TryToFindPaymentAsync(
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
                var (isRefunded, isRefundedError) = await TezosSwapRefundedHelper.IsRefundedAsync(
                        swap: swap,
                        currency: XtzConfig,
                        attempts: MaxRefundCheckAttempts,
                        attemptIntervalInSec: RefundCheckAttemptIntervalInSec,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                if (isRefundedError == null)
                {
                    if (isRefunded)
                    {
                        await RefundConfirmedEventHandler(swap, cancellationToken)
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
                    .LocalStorage
                    .GetTransactionByIdAsync<TezosOperation>(XtzConfig.Name, txId)
                    .ConfigureAwait(false);

                if (tx is not { IsConfirmed: true }) continue;

                return tx.IsConfirmed;
            }

            return false;
        }

        private string GetInitiateParameters(
            Swap swap,
            long refundTimestampInUtcSec,
            long redeemFeeInMtz)
        {
            return $"{{\"prim\":\"Pair\",\"args\":[{{\"string\":\"{swap.PartyAddress}\"}},{{\"prim\":\"Pair\",\"args\":[{{\"prim\":\"Pair\",\"args\":[{{\"bytes\":\"{swap.SecretHash.ToHexString()}\"}},{{\"int\":\"{refundTimestampInUtcSec}\"}}]}},{{\"int\":\"{redeemFeeInMtz}\"}}]}}]}}";
        }

        private string GetRedeemParameters(Swap swap)
        {
            return $"{{\"bytes\":\"{swap.Secret.ToHexString()}\"}}";
        }

        private string GetRefundParameters(Swap swap)
        {
            return $"{{\"bytes\":\"{swap.SecretHash.ToHexString()}\"}}";
        }

        #endregion Helpers
    }
}