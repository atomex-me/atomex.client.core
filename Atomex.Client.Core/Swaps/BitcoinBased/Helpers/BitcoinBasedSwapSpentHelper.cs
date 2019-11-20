using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Atomex.Blockchain.Abstract;
using Atomex.Blockchain.BitcoinBased;
using Atomex.Blockchain.BitcoinBased.Helpers;
using Atomex.Core;
using Atomex.Core.Entities;
using Serilog;

namespace Atomex.Swaps.BitcoinBased.Helpers
{
    public static class BitcoinBasedSwapSpentHelper
    {
        public static Task StartSwapSpentControlAsync(
            ClientSwap swap,
            Currency currency,
            DateTime refundTimeUtc,
            TimeSpan interval,
            Action<ClientSwap, ITxPoint, CancellationToken> completionHandler = null,
            Action<ClientSwap, CancellationToken> refundTimeReachedHandler = null,
            CancellationToken cancellationToken = default)
        {
            var swapOutput = ((IBitcoinBasedTransaction)swap.PaymentTx)
                .Outputs
                .Cast<BitcoinBasedTxOutput>()
                .FirstOrDefault(o => o.IsPayToScriptHash(Convert.FromBase64String(swap.RedeemScript)));

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

                    if (!result.HasError)
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