using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Newtonsoft.Json.Linq;
using Serilog;

using Atomex.Blockchain.Tezos;
using Atomex.Common;
using Atomex.Core;
using Atomex.Swaps.Abstract;
using Atomex.TezosTokens;

namespace Atomex.Swaps.Tezos.FA2.Helpers
{
    public static class Fa2SwapRedeemedHelper
    {
        public static async Task<Result<byte[]>> IsRedeemedAsync(
            Swap swap,
            CurrencyConfig currency,
            TezosConfig tezos,
            CancellationToken cancellationToken = default)
        {
            try
            {
                Log.Debug("Tezos FA2: check redeem event");

                var fa2 = (Fa2Config)currency;

                var contractAddress = fa2.SwapContractAddress;

                var blockchainApi = (ITezosBlockchainApi)tezos.BlockchainApi;

                var txsResult = await blockchainApi
                    .TryGetTransactionsAsync(contractAddress, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                if (txsResult == null)
                    return new Error(Errors.RequestError, $"Connection error while getting txs from contract {contractAddress}");

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
                        if (tx.To == contractAddress && IsSwapRedeem(tx, swap.SecretHash))
                        {
                            // redeem!
                            var secret = GetSecret(tx);

                            Log.Debug("Redeem event received with secret {@secret}", Convert.ToBase64String(secret));

                            return secret;
                        }

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
                Log.Error(e, "Tezos FA2 redeem control task error");

                return new Error(Errors.InternalError, e.Message);
            }

            return (byte[])null;
        }

        public static async Task<Result<byte[]>> IsRedeemedAsync(
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

                var isRedeemedResult = await IsRedeemedAsync(
                        swap: swap,
                        currency: currency,
                        tezos: tezos,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                if (isRedeemedResult.HasError && isRedeemedResult.Error.Code != Errors.RequestError) // has error
                    return isRedeemedResult;

                if (!isRedeemedResult.HasError)
                    return isRedeemedResult;

                await Task.Delay(TimeSpan.FromSeconds(attemptIntervalInSec), cancellationToken)
                    .ConfigureAwait(false);
            }

            return new Error(Errors.MaxAttemptsCountReached, "Max attempts count reached for redeem check");
        }

        public static Task StartSwapRedeemedControlAsync(
            Swap swap,
            CurrencyConfig currency,
            TezosConfig tezos,
            DateTime refundTimeUtc,
            TimeSpan interval,
            Func<Swap, byte[], CancellationToken, Task> redeemedHandler = null,
            Func<Swap, DateTime, CancellationToken, Task> canceledHandler = null,
            CancellationToken cancellationToken = default)
        {
            Log.Debug("StartSwapRedeemedControlAsync for {@Currency} swap with id {@swapId} started", currency.Name, swap.Id);

            return Task.Run(async () =>
            {
                try
                {
                    while (!cancellationToken.IsCancellationRequested)
                    {
                        var isRedeemedResult = await IsRedeemedAsync(
                                swap: swap,
                                currency: currency,
                                tezos: tezos,
                                cancellationToken: cancellationToken)
                            .ConfigureAwait(false);

                        if (isRedeemedResult.HasError)
                            Log.Error("{@currency} IsRedeemedAsync error for swap {@swap}. Code: {@code}. Description: {@desc}",
                                currency.Name,
                                swap.Id,
                                isRedeemedResult.Error.Code,
                                isRedeemedResult.Error.Description);

                        if (!isRedeemedResult.HasError && isRedeemedResult.Value != null) // has secret
                        {
                            await redeemedHandler
                                .Invoke(swap, isRedeemedResult.Value, cancellationToken)
                                .ConfigureAwait(false);

                            break;
                        }

                        if (DateTime.UtcNow >= refundTimeUtc)
                        {
                            await canceledHandler
                                .Invoke(swap, refundTimeUtc, cancellationToken)
                                .ConfigureAwait(false);

                            break;
                        }

                        await Task.Delay(interval, cancellationToken)
                            .ConfigureAwait(false);
                    }

                    Log.Debug("StartSwapRedeemedControlAsync for {@Currency} swap with id {@swapId} {@message}",
                        currency.Name,
                        swap.Id,
                        cancellationToken.IsCancellationRequested ? "canceled" : "completed");
                }
                catch (OperationCanceledException)
                {
                    Log.Debug("StartSwapRedeemedControlAsync for {@Currency} swap with id {@swapId} canceled",
                        currency.Name,
                        swap.Id);
                }
                catch (Exception e)
                {
                    Log.Error(e, "StartSwapRedeemedControlAsync for {@Currency} swap with id {@swapId} error",
                        currency.Name,
                        swap.Id);
                }

            }, cancellationToken);
        }

        public static bool IsSwapRedeem(TezosTransaction tx, byte[] secretHash)
        {
            try
            {
                if (tx.Params == null)
                    return false;

                var entrypoint = tx.Params?["entrypoint"]?.ToString();

                var paramSecretHex = entrypoint switch
                {
                    "default" => GetSecret(tx.Params?["value"]?["args"]?[0]?["args"]?[0]),
                    "redeem" => GetSecret(tx.Params?["value"]),
                    _ => null
                };

                if (paramSecretHex == null)
                    return false;

                var paramSecretBytes = Hex.FromString(paramSecretHex);
                var paramSecretHashBytes = CurrencySwap.CreateSwapSecretHash(paramSecretBytes);

                return paramSecretHashBytes.SequenceEqual(secretHash);
            }
            catch (Exception)
            {
                return false;
            }
        }

        public static byte[] GetSecret(TezosTransaction tx)
        {
            if (tx.Params == null)
                return null;

            var entrypoint = tx.Params?["entrypoint"]?.ToString();

            var secretHex = entrypoint switch
            {
                "default" => GetSecret(tx.Params?["value"]?["args"]?[0]?["args"]?[0]),
                "redeem" => GetSecret(tx.Params?["value"]),
                _ => null
            };

            return Hex.FromString(secretHex);
        }

        private static string GetSecret(JToken redeemParams)
        {
            return redeemParams?["bytes"]?.Value<string>();
        }
    }
}