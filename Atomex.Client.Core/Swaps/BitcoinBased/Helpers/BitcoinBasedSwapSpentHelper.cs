using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using NBitcoin;
using Serilog;

using Atomex.Blockchain.Bitcoin;
using Atomex.Blockchain.Bitcoin.Helpers;
using Atomex.Common;
using Atomex.Core;
using Atomex.Swaps.Abstract;
using Atomex.Swaps.Helpers;
using Atomex.Wallet.Abstract;

namespace Atomex.Swaps.BitcoinBased.Helpers
{
    public static class BitcoinBasedSwapSpentHelper
    {
        public static async Task StartSwapSpentControlAsync(
            Swap swap,
            BitcoinBasedConfig currencyConfig,
            ILocalStorage localStorage,
            DateTime refundTimeUtc,
            TimeSpan interval,
            Func<Swap, BitcoinTxPoint, CancellationToken, Task> completionHandler = null,
            Func<Swap, CancellationToken, Task> refundTimeReachedHandler = null,
            CancellationToken cancellationToken = default)
        {
            Log.Debug("StartSwapSpentControlAsync for {@Currency} swap with id {@swapId} started", currencyConfig.Name, swap.Id);

            try
            {
                var side = swap.Symbol
                    .OrderSideForBuyCurrency(swap.PurchasedCurrency);

                var requiredAmount = AmountHelper.QtyToSellAmount(side, swap.Qty, swap.Price, currencyConfig.Precision);
                var requiredAmountInSatoshi = currencyConfig.CoinToSatoshi(requiredAmount);

                var lockTimeInSeconds = swap.IsInitiator
                    ? CurrencySwap.DefaultInitiatorLockTimeInSeconds
                    : CurrencySwap.DefaultAcceptorLockTimeInSeconds;

                var refundTimeUtcInSec = new DateTimeOffset(swap.TimeStamp.ToUniversalTime().AddSeconds(lockTimeInSeconds))
                    .ToUnixTimeSeconds();

                var redeemScript = swap.RefundAddress == null && swap.RedeemScript != null
                    ? new Script(Convert.FromBase64String(swap.RedeemScript))
                    : BitcoinSwapTemplate
                        .CreateHtlcP2PkhSwapPayment(
                            aliceRefundAddress: swap.RefundAddress,
                            bobAddress: swap.PartyAddress,
                            lockTimeStamp: refundTimeUtcInSec,
                            secretHash: swap.SecretHash,
                            secretSize: CurrencySwap.DefaultSecretSize,
                            expectedNetwork: currencyConfig.Network);

                var (paymentTx, findPaymentError) = await TransactionsHelper
                    .TryFindTransaction<BitcoinTransaction>(
                        txId: swap.PaymentTxId,
                        currency: swap.SoldCurrency,
                        localStorage: localStorage,
                        blockchainApi: currencyConfig.GetBlockchainApi(),
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                if (findPaymentError != null)
                {
                    throw new InternalException(
                        code: findPaymentError.Value.Code,
                        description: "Find payment tx error: " + findPaymentError.Value.Message);
                }

                var swapOutput = paymentTx
                    .Outputs
                    .Cast<BitcoinTxOutput>()
                    .FirstOrDefault(o => o.IsPayToScriptHash(redeemScript) && o.Value >= requiredAmountInSatoshi);

                if (swapOutput == null)
                {
                    throw new InternalException(
                        code: Errors.SwapError,
                        description: "Payment tx have not swap output");
                }

                while (!cancellationToken.IsCancellationRequested)
                {
                    Log.Debug("Output spent control for {@currency} swap {@swapId}", currencyConfig.Name, swap.Id);

                    var (spentPoint, error) = await currencyConfig
                        .GetSpentPointAsync(
                            hash: swap.PaymentTxId,
                            index: swapOutput.Index,
                            cancellationToken: cancellationToken)
                        .ConfigureAwait(false);

                    if (error == null && spentPoint != null)
                    {
                        await completionHandler
                            .Invoke(swap, spentPoint, cancellationToken)
                            .ConfigureAwait(false);

                        break;
                    }

                    if (DateTime.UtcNow >= refundTimeUtc)
                    {
                        await refundTimeReachedHandler
                            .Invoke(swap, cancellationToken)
                            .ConfigureAwait(false);

                        break;
                    }

                    await Task.Delay(interval, cancellationToken)
                        .ConfigureAwait(false);
                }

                Log.Debug("StartSwapSpentControlAsync for {@Currency} swap with id {@swapId} {@message}",
                    currencyConfig.Name,
                    swap.Id,
                    cancellationToken.IsCancellationRequested ? "canceled" : "completed");
            }
            catch (OperationCanceledException)
            {
                Log.Debug("StartSwapSpentControlAsync for {@Currency} swap with id {@swapId} canceled",
                    currencyConfig.Name,
                    swap.Id);
            }
            catch (Exception e)
            {
                Log.Error(e, "StartSwapSpentControlAsync for {@Currency} swap with id {@swapId} error",
                    currencyConfig.Name,
                    swap.Id);
            }
        }
    }
}