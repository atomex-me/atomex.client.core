using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using NBitcoin;
using Serilog;

using Atomex.Blockchain.Abstract;
using Atomex.Blockchain.BitcoinBased;
using Atomex.Blockchain.BitcoinBased.Helpers;
using Atomex.Common;
using Atomex.Core;
using Atomex.Swaps.Abstract;

namespace Atomex.Swaps.BitcoinBased.Helpers
{
    public static class BitcoinBasedSwapSpentHelper
    {
        public static Task StartSwapSpentControlAsync(
            Swap swap,
            CurrencyConfig currency,
            DateTime refundTimeUtc,
            TimeSpan interval,
            Func<Swap, ITxPoint, CancellationToken, Task> completionHandler = null,
            Func<Swap, CancellationToken, Task> refundTimeReachedHandler = null,
            CancellationToken cancellationToken = default)
        {
            Log.Debug("StartSwapSpentControlAsync for {@Currency} swap with id {@swapId} started", currency.Name, swap.Id);

            return Task.Run(async () =>
            {
                try
                {
                    var bitcoinBased = (BitcoinBasedConfig)currency;

                    var side = swap.Symbol
                        .OrderSideForBuyCurrency(swap.PurchasedCurrency);

                    var requiredAmount = AmountHelper.QtyToSellAmount(side, swap.Qty, swap.Price, bitcoinBased.DigitsMultiplier);
                    var requiredAmountInSatoshi = bitcoinBased.CoinToSatoshi(requiredAmount);

                    var lockTimeInSeconds = swap.IsInitiator
                        ? CurrencySwap.DefaultInitiatorLockTimeInSeconds
                        : CurrencySwap.DefaultAcceptorLockTimeInSeconds;

                    var refundTimeUtcInSec = new DateTimeOffset(swap.TimeStamp.ToUniversalTime().AddSeconds(lockTimeInSeconds))
                        .ToUnixTimeSeconds();

                    var redeemScript = swap.RefundAddress == null && swap.RedeemScript != null
                        ? new Script(Convert.FromBase64String(swap.RedeemScript))
                        : BitcoinBasedSwapTemplate
                            .GenerateHtlcP2PkhSwapPayment(
                                aliceRefundAddress: swap.RefundAddress,
                                bobAddress: swap.PartyAddress,
                                lockTimeStamp: refundTimeUtcInSec,
                                secretHash: swap.SecretHash,
                                secretSize: CurrencySwap.DefaultSecretSize,
                                expectedNetwork: bitcoinBased.Network);

                    var swapOutput = ((IBitcoinBasedTransaction)swap.PaymentTx)
                        .Outputs
                        .Cast<BitcoinBasedTxOutput>()
                        .FirstOrDefault(o => o.IsPayToScriptHash(redeemScript) && o.Value >= requiredAmountInSatoshi);

                    if (swapOutput == null)
                        throw new InternalException(
                            code: Errors.SwapError,
                            description: "Payment tx have not swap output");

                    while (!cancellationToken.IsCancellationRequested)
                    {
                        Log.Debug("Output spent control for {@currency} swap {@swapId}", currency.Name, swap.Id);

                        var result = await currency
                            .GetSpentPointAsync(
                                hash: swap.PaymentTxId,
                                index: swapOutput.Index,
                                cancellationToken: cancellationToken)
                            .ConfigureAwait(false);

                        if (result != null && !result.HasError && result.Value != null)
                        {
                            await completionHandler
                                .Invoke(swap, result.Value, cancellationToken)
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
                        currency.Name,
                        swap.Id,
                        cancellationToken.IsCancellationRequested ? "canceled" : "completed");
                }
                catch (OperationCanceledException)
                {
                    Log.Debug("StartSwapSpentControlAsync for {@Currency} swap with id {@swapId} canceled",
                        currency.Name,
                        swap.Id);
                }
                catch (Exception e)
                {
                    Log.Error(e, "StartSwapSpentControlAsync for {@Currency} swap with id {@swapId} error",
                        currency.Name,
                        swap.Id);
                }

            }, cancellationToken);
        }
    }
}