using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Newtonsoft.Json.Linq;
using Serilog;

using Atomex.Blockchain.Tezos;
using Atomex.Common;
using Atomex.Core;

namespace Atomex.Swaps.Tezos.Helpers
{
    public static class TezosSwapRefundedHelper
    {
        public static async Task<Result<bool>> IsRefundedAsync(
            Swap swap,
            CurrencyConfig currency,
            CancellationToken cancellationToken = default)
        {
            try
            {
                Log.Debug("Tezos: check refund event");

                var tezos = (Atomex.TezosConfig)currency;

                var contractAddress = tezos.SwapContractAddress;

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
                Log.Error(e, "Tezos refund control task error");

                return new Error(Errors.InternalError, e.Message);
            }

            return false;
        }

        public static async Task<Result<bool>> IsRefundedAsync(
            Swap swap,
            CurrencyConfig currency,
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

                if (entrypoint == "default" && tx.Params?["value"]?["prim"]?.Value<string>() != "Right")
                    return false;

                var paramSecretHashInHex = entrypoint switch
                {
                    "default"  => GetSecretHash(tx.Params?["value"]?["args"]?[0]?["args"]?[0]),
                    "withdraw" => GetSecretHash(tx.Params?["value"]?["args"]?[0]),
                    "refund"   => GetSecretHash(tx.Params?["value"]),
                    _          => ""
                };

                if (paramSecretHashInHex == null)
                    return false;

                var paramSecretHashBytes = Hex.FromString(paramSecretHashInHex);

                return paramSecretHashBytes.SequenceEqual(secretHash);
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static string GetSecretHash(JToken refundParams) =>
            refundParams?["bytes"]?.Value<string>();
    }
}