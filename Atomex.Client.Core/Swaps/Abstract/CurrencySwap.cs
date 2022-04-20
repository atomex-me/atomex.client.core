using System;
using System.Threading;
using System.Threading.Tasks;
using Serilog;

using Atomex.Abstract;
using Atomex.Blockchain.Abstract;
using Atomex.Core;
using Atomex.Common;
using Atomex.Cryptography;
using Atomex.Wallet.Abstract;

namespace Atomex.Swaps.Abstract
{
    public abstract class CurrencySwap : ICurrencySwap
    {
        public const int DefaultSecretSize = 32;
        public const int DefaultSecretHashSize = 32; //20;

        public const int DefaultInitiatorLockTimeInSeconds = 10 * 60 * 60; // 10 hours
        public const int DefaultAcceptorLockTimeInSeconds = 5 * 60 * 60; // 5 hours
        protected const int DefaultGetTransactionAttempts = 10;

        protected static TimeSpan ConfirmationCheckInterval = TimeSpan.FromSeconds(60);
        protected static TimeSpan OutputSpentCheckInterval = TimeSpan.FromSeconds(60);
        protected static TimeSpan GetTransactionInterval = TimeSpan.FromSeconds(60);
        protected static TimeSpan RefundTimeCheckInterval = TimeSpan.FromSeconds(60);
        protected static TimeSpan ForceRefundInterval = TimeSpan.FromMinutes(5);
        public static TimeSpan RedeemTimeReserve = TimeSpan.FromMinutes(90);
        protected static TimeSpan PartyRedeemTimeReserve = TimeSpan.FromMinutes(95);
        public static TimeSpan PaymentTimeReserve = TimeSpan.FromMinutes(60);
        protected static TimeSpan RefundDelay = TimeSpan.FromSeconds(30);

        public OnSwapUpdatedAsyncDelegate InitiatorPaymentConfirmed { get; set; }
        public OnSwapUpdatedAsyncDelegate AcceptorPaymentConfirmed { get; set; }
        public OnSwapUpdatedAsyncDelegate AcceptorPaymentSpent { get; set; }
        public OnSwapUpdatedAsyncDelegate SwapUpdated { get; set; }

        public string Currency { get; }
        protected readonly ICurrencies Currencies;

        protected CurrencySwap(
            string currency,
            ICurrencies currencies)
        {
            Currency = currency;
            Currencies = currencies ?? throw new ArgumentNullException(nameof(currencies));
        }

        public abstract Task PayAsync(
            Swap swap,
            CancellationToken cancellationToken = default);

        public abstract Task StartPartyPaymentControlAsync(
            Swap swap,
            CancellationToken cancellationToken = default);

        public abstract Task RedeemAsync(
            Swap swap,
            CancellationToken cancellationToken = default);

        public abstract Task RedeemForPartyAsync(
            Swap swap,
            CancellationToken cancellationToken = default);

        public abstract Task RefundAsync(
            Swap swap,
            CancellationToken cancellationToken = default);

        public abstract Task StartWaitForRedeemAsync(
            Swap swap,
            CancellationToken cancellationToken = default);

        public abstract Task StartWaitForRedeemBySomeoneAsync(
            Swap swap,
            CancellationToken cancellationToken = default);

        public abstract Task<Result<IBlockchainTransaction>> TryToFindPaymentAsync(
            Swap swap,
            CancellationToken cancellationToken = default);

        protected Task RaiseInitiatorPaymentConfirmed(
            Swap swap,
            CancellationToken cancellationToken = default) =>
            InitiatorPaymentConfirmed?.Invoke(this, new SwapEventArgs(swap), cancellationToken);

        protected Task RaiseAcceptorPaymentConfirmed(
            Swap swap,
            CancellationToken cancellationToken = default) =>
            AcceptorPaymentConfirmed?.Invoke(this, new SwapEventArgs(swap), cancellationToken);

        protected Task RaiseAcceptorPaymentSpent(
            Swap swap,
            CancellationToken cancellationToken = default) =>
            AcceptorPaymentSpent?.Invoke(this, new SwapEventArgs(swap), cancellationToken);

        protected Task UpdateSwapAsync(
            Swap swap,
            SwapStateFlags changedFlag,
            CancellationToken cancellationToken = default) =>
            SwapUpdated?.Invoke(this, new SwapEventArgs(swap, changedFlag), cancellationToken);

        public static byte[] CreateSwapSecret() =>
            Rand.SecureRandomBytes(DefaultSecretSize);

        public static byte[] CreateSwapSecretHash(byte[] secretBytes) =>
            Sha256.Compute(secretBytes, 2);

        protected Task TrackTransactionConfirmationAsync(
            Swap swap,
            CurrencyConfig currency,
            IAccountDataRepository dataRepository,
            string txId,
            Func<Swap, IBlockchainTransaction, CancellationToken, Task> confirmationHandler,
            CancellationToken cancellationToken = default)
        {
            return Task.Run(async () =>
            {
                try
                {
                    while (!cancellationToken.IsCancellationRequested)
                    {
                        var tx = await dataRepository
                            .GetTransactionByIdAsync(currency.Name, txId, currency.TransactionType)
                            .ConfigureAwait(false);

                        if (tx == null)
                            break;

                        if (tx.IsConfirmed)
                        {
                            await confirmationHandler.Invoke(swap, tx, cancellationToken)
                                .ConfigureAwait(false);

                            break;
                        }

                        await Task.Delay(ConfirmationCheckInterval, cancellationToken)
                            .ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException)
                {
                    // nothing to do...
                }
                catch (Exception e)
                {
                    Log.Error(e, $"{Currency} TrackTransactionConfirmationAsync error");
                }

            }, cancellationToken);
        }

        protected Task ControlRefundTimeAsync(
            Swap swap,
            DateTime refundTimeUtc,
            Func<Swap, CancellationToken, Task> refundTimeReachedHandler,
            CancellationToken cancellationToken = default)
        {
            return Task.Run(async () =>
            {
                try
                {
                    while (!cancellationToken.IsCancellationRequested)
                    {
                        Log.Debug("Refund time check for swap {@swapId}", swap.Id);

                        var refundTimeReached = DateTime.UtcNow >= refundTimeUtc;

                        if (refundTimeReached)
                        {
                            await refundTimeReachedHandler.Invoke(swap, cancellationToken)
                                .ConfigureAwait(false);

                            break;
                        }

                        await Task.Delay(RefundTimeCheckInterval, cancellationToken)
                            .ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException)
                {
                    Log.Debug("ControlRefundTimeAsync canceled.");
                }
                catch (Exception e)
                {
                    Log.Error(e, "ControlRefundTimeAsync error");
                }

            }, cancellationToken);
        }

        protected async Task<bool> CheckPayRelevanceAsync(
            Swap swap,
            CancellationToken cancellationToken = default)
        {
            if (swap.IsAcceptor)
            {
                var acceptorRefundTimeUtc = swap.TimeStamp
                    .ToUniversalTime()
                    .AddSeconds(DefaultAcceptorLockTimeInSeconds);

                var paymentDeadline = acceptorRefundTimeUtc - PaymentTimeReserve;

                if (DateTime.UtcNow > paymentDeadline)
                {
                    Log.Error("Payment deadline reached for swap {@swap}", swap.Id);

                    swap.StateFlags |= SwapStateFlags.IsCanceled;

                    await UpdateSwapAsync(swap, SwapStateFlags.IsCanceled, cancellationToken)
                        .ConfigureAwait(false);

                    return false;
                }
            }

            return true;
        }

        protected async Task SwapInitiatedHandler(
            Swap swap,
            CancellationToken cancellationToken = default)
        {
            Log.Debug(
                "Initiator payment transaction received. Now counter party can broadcast payment tx for swap {@swapId}",
                swap.Id);

            swap.StateFlags |= SwapStateFlags.HasPartyPayment;
            swap.StateFlags |= SwapStateFlags.IsPartyPaymentConfirmed;

            await UpdateSwapAsync(swap, SwapStateFlags.HasPartyPayment | SwapStateFlags.IsPartyPaymentConfirmed, cancellationToken)
                .ConfigureAwait(false);

            await InitiatorPaymentConfirmed.Invoke(this, new SwapEventArgs(swap), cancellationToken)
                .ConfigureAwait(false);
        }

        protected async Task SwapAcceptedHandler(
            Swap swap,
            CancellationToken cancellationToken = default)
        {
            try
            {
                Log.Debug(
                    "Acceptors payment transaction received. Now initiator can do self redeem and do party redeem for acceptor (if needs and wants) for swap {@swapId}.",
                    swap.Id);

                swap.StateFlags |= SwapStateFlags.HasPartyPayment;
                swap.StateFlags |= SwapStateFlags.IsPartyPaymentConfirmed;

                await UpdateSwapAsync(swap, SwapStateFlags.HasPartyPayment | SwapStateFlags.IsPartyPaymentConfirmed, cancellationToken)
                    .ConfigureAwait(false);

                await RaiseAcceptorPaymentConfirmed(swap, cancellationToken)
                    .ConfigureAwait(false);

                await RedeemAsync(swap, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception e)
            {
                Log.Error(e, "Swap accepted error");
            }
        }

        protected Task SwapCanceledHandler(
            Swap swap,
            CancellationToken cancellationToken = default)
        {
            // todo: do smth here
            Log.Debug("Swap canceled due to wrong counter party params {@swapId}", swap.Id);

            return Task.CompletedTask;
        }

        protected async Task RedeemConfirmedEventHandler(
            Swap swap,
            IBlockchainTransaction tx,
            CancellationToken cancellationToken = default)
        {
            swap.StateFlags |= SwapStateFlags.IsRedeemConfirmed;

            await UpdateSwapAsync(swap, SwapStateFlags.IsRedeemConfirmed, cancellationToken)
                .ConfigureAwait(false);
        }

        protected async Task RedeemCompletedEventHandler(
            Swap swap,
            byte[] secret,
            CancellationToken cancellationToken = default)
        {
            Log.Debug("Handle redeem control completed event for swap {@swapId}", swap.Id);

            if (swap.IsAcceptor)
            {
                swap.Secret = secret;

                await UpdateSwapAsync(swap, SwapStateFlags.HasSecret, cancellationToken)
                    .ConfigureAwait(false);

                await RaiseAcceptorPaymentSpent(swap, cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        protected Task RedeemCanceledEventHandler(
            Swap swap,
            DateTime refundTimeUtc,
            CancellationToken cancellationToken = default)
        {
            Log.Debug("Handle redeem control canceled event for swap {@swapId}", swap.Id);

            _ = ControlRefundTimeAsync(
                swap: swap,
                refundTimeUtc: refundTimeUtc,
                refundTimeReachedHandler: RefundTimeReachedHandler,
                cancellationToken: cancellationToken);

            return Task.CompletedTask;
        }

        protected async Task RefundConfirmedEventHandler(
            Swap swap,
            IBlockchainTransaction tx,
            CancellationToken cancellationToken = default)
        {
            swap.StateFlags |= SwapStateFlags.IsRefundConfirmed;

            await UpdateSwapAsync(swap, SwapStateFlags.IsRefundConfirmed, cancellationToken)
                .ConfigureAwait(false);
        }

        protected abstract Task RefundTimeReachedHandler(
            Swap swap,
            CancellationToken cancellationToken = default);

        protected async Task<bool> RefundTimeDelayAsync(
            DateTime refundTimeUtc,
            CancellationToken cancellationToken = default)
        {
            // if refund time has not come
            if (DateTime.UtcNow < refundTimeUtc)
                return false;

            var timeDiff = DateTime.UtcNow - refundTimeUtc;

            // if refund time came less than RefundDelay seconds ago
            if (timeDiff < RefundDelay)
                await Task.Delay(RefundDelay - timeDiff, cancellationToken)
                    .ConfigureAwait(false);

            return true;
        }
    }
}