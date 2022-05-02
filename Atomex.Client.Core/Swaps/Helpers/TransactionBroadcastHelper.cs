using System;
using System.Threading;
using System.Threading.Tasks;

using Serilog;

using Atomex.Core;
using Atomex.Blockchain.Abstract;

namespace Atomex.Swaps.Helpers
{
    public static class TransactionBroadcastHelper
    {
        public static Task<string> ForceBroadcast(
            this IBlockchainTransaction_OLD tx,
            IBlockchainApi_OLD blockchainApi,
            Swap swap,
            TimeSpan interval,
            Action<Swap, string, CancellationToken> completionHandler = null,
            CancellationToken cancellationToken = default)
        {
            return Task.Run(async () =>
            {
                try
                {
                    while (!cancellationToken.IsCancellationRequested)
                    {
                        var broadcastResult = await blockchainApi
                            .TryBroadcastAsync(tx, cancellationToken: cancellationToken)
                            .ConfigureAwait(false);

                        if (!broadcastResult.HasError)
                        {
                            if (broadcastResult.Value != null)
                            {
                                completionHandler?.Invoke(swap, broadcastResult.Value, cancellationToken);
                                return broadcastResult.Value;
                            }
                        }
                        else
                        {
                            Log.Error("Error while broadcast {@currency} tx with. Code: {@code}. Description: {@desc}",
                                tx.Currency,
                                broadcastResult.Error.Code,
                                broadcastResult.Error.Description);
                        }

                        await Task.Delay(interval, cancellationToken)
                            .ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException)
                {
                    Log.Debug("ForceBroadcast canceled.");
                }
                catch (Exception e)
                {
                    Log.Error(e, "Error while broadcast {@currency} tx.", tx.Currency);
                }

                return null;

            }, cancellationToken);
        }
    }
}