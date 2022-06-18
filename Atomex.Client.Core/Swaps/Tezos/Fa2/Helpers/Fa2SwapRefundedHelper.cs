using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Newtonsoft.Json.Linq;
using Serilog;

using Atomex.Blockchain.Tezos;
using Atomex.Common;
using Atomex.Core;
using Atomex.TezosTokens;

namespace Atomex.Swaps.Tezos.FA2.Helpers
{
    public static class Fa2SwapRefundedHelper
    {
        public static async Task<Result<bool>> IsRefundedAsync(
            Swap swap,
            CurrencyConfig currency,
            TezosConfig tezos,
            CancellationToken cancellationToken = default)
        {
            try
            {
                Log.Debug("Tezos FA2: check refund event");

                var fa2 = (Fa2Config)currency;

                var contractAddress = fa2.SwapContractAddress;

                var blockchainApi = (ITezosBlockchainApi)tezos.BlockchainApi;

                var txsResult = await blockchainApi
                    .TryGetTransactionsAsync(contractAddress, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                if (txsResult == null)
                    return new Error(Errors.RequestError, $"Connection error while getting {contractAddress} transactions");

                if (txsResult.HasError)
                {
                    Log.Error("Error while get transactions from contract {@contract}. Code: {@code}. Description: {@desc}",
                        contractAddress,
                        txsResult.Error.Code,
                        txsResult.Error.Description);

                    return txsResult.Error;
                }

                var txs = txsResult.Value
                    ?.Cast<TezosTransaction>()
                    .ToList();

                if (txs != null)
                {
                    foreach (var tx in txs)
                    {
                        if (tx.To == contractAddress && IsSwapRefund(tx, swap.SecretHash))
                            return true;

                        if (tx.BlockInfo?.BlockTime == null)
                            continue;

                        var blockTimeUtc = tx.BlockInfo.BlockTime.Value.ToUniversalTime();
                        var swapTimeUtc = swap.TimeStamp.ToUniversalTime();

                        if (blockTimeUtc < swapTimeUtc)
                            break;
                    }
                }
            }
            catch (Exception e)
            {
                Log.Error(e, "Tezos FA2 refund control task error");

                return new Error(Errors.InternalError, e.Message);
            }

            return false;
        }

        public static async Task<Result<bool>> IsRefundedAsync(
            Swap swap,
            CurrencyConfig currency,
            TezosConfig tezos,
            int attempts,
            int attemptIntervalInSec,
            CancellationToken cancellationToken = default)
        {
            var attempt = 0;

            while (!cancellationToken.IsCancellationRequested && attempt < attempts)
            {
                ++attempt;

                var isRefundedResult = await IsRefundedAsync(
                        swap: swap,
                        currency: currency,
                        tezos: tezos,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                if (isRefundedResult.HasError && isRefundedResult.Error.Code != Errors.RequestError) // has error
                    return isRefundedResult;

                if (!isRefundedResult.HasError)
                    return isRefundedResult;

                await Task.Delay(TimeSpan.FromSeconds(attemptIntervalInSec), cancellationToken)
                    .ConfigureAwait(false);
            }

            return new Error(Errors.MaxAttemptsCountReached, "Max attempts count reached for refund check");
        }

        public static bool IsSwapRefund(TezosTransaction tx, byte[] secretHash)
        {
            try
            {
                if (tx.Params == null)
                    return false;

                var entrypoint = tx.Params?["entrypoint"]?.ToString();

                var paramSecretHash = entrypoint switch
                {
                    "default" => GetSecretHash(tx.Params?["value"]?["args"]?[0]),
                    "refund" => GetSecretHash(tx.Params?["value"]),
                    _ => null
                };

                if (paramSecretHash == null)
                    return false;

                var paramSecretHashBytes = Hex.FromString(paramSecretHash);

                return paramSecretHashBytes.SequenceEqual(secretHash);
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static string GetSecretHash(JToken refundParams)
        {
            return refundParams?["bytes"]?.Value<string>();
        }
    }
}