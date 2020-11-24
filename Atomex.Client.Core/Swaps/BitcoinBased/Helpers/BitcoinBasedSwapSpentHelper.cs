using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Atomex.Blockchain.Abstract;
using Atomex.Blockchain.BitcoinBased;
using Atomex.Blockchain.BitcoinBased.Helpers;
using Atomex.Common;
using Atomex.Core;
using Atomex.Swaps.Abstract;
using NBitcoin;
using Serilog;

namespace Atomex.Swaps.BitcoinBased.Helpers
{
    public static class BitcoinBasedSwapSpentHelper
    {
        public static Task StartSwapSpentControlAsync(
            Swap swap,
            Currency currency,
            DateTime refundTimeUtc,
            TimeSpan interval,
            Action<Swap, ITxPoint, CancellationToken> completionHandler = null,
            Action<Swap, CancellationToken> refundTimeReachedHandler = null,
            CancellationToken cancellationToken = default)
        {
            var bitcoinBased = (BitcoinBasedCurrency)currency;

            var side = swap.Symbol
                .OrderSideForBuyCurrency(swap.PurchasedCurrency);

            var requiredAmount = AmountHelper.QtyToAmount(side, swap.Qty, swap.Price, bitcoinBased.DigitsMultiplier);
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

            return Task.Run(async () =>
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    Log.Debug("Output spent control for {@currency} swap {@swapId}", currency.Name, swap.Id);

                    var result = await currency
                        .GetSpentPointAsync(
                            hash: swap.PaymentTxId,
                            index: swapOutput.Index,
                            cancellationToken: cancellationToken)
                        .ConfigureAwait(false);

                    if (result != null && !result.HasError)
                    {
                        if (result.Value != null)
                        {
                            completionHandler?.Invoke(swap, result.Value, cancellationToken);
                            break;
                        }
                    }

                    if (DateTime.UtcNow >= refundTimeUtc)
                    {
                        refundTimeReachedHandler?.Invoke(swap, cancellationToken);
                        break;
                    }

                    await Task.Delay(interval, cancellationToken)
                        .ConfigureAwait(false);
                }
            }, cancellationToken);
        }
    }
}