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
            this ITransaction tx,
            IBlockchainApi blockchainApi,
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
                        var (txId, error) = await blockchainApi
                            .BroadcastAsync(tx, cancellationToken: cancellationToken)
                            .ConfigureAwait(false);

                        if (error == null)
                        {
                            if (txId != null)
                            {
                                completionHandler?.Invoke(swap, txId, cancellationToken);
                                return txId;
                            }
                        }
                        else
                        {
                            Log.Error("Error while broadcast {@currency} tx with. Code: {@code}. Description: {@desc}",
                                tx.Currency,
                                error.Value.Code,
                                error.Value.Message);
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