using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Atomex.Blockchain.Tezos;
using Atomex.Common;
using Atomex.Core;
using Serilog;

namespace Atomex.Swaps.Tezos.Helpers
{
    public static class TezosSwapRedeemedHelper
    {
        public static async Task<Result<byte[]>> IsRedeemedAsync(
            Swap swap,
            Currency currency,
            CancellationToken cancellationToken = default)
        {
            try
            {
                Log.Debug("Tezos: check redeem event");

                var tezos = (Atomex.Tezos)currency;

                var contractAddress = tezos.SwapContractAddress;

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
                        if (tx.To == contractAddress && tx.IsSwapRedeem(swap.SecretHash))
                        {
                            // redeem!
                            var secret = tx.GetSecret();

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
                Console.WriteLine("Tezos redeem control task error");

                return new Error(Errors.InternalError, e.Message);
            }

            return (byte[])null;
        }

        public static async Task<Result<byte[]>> IsRedeemedAsync(
            Swap swap,
            Currency currency,
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
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                if (isRedeemedResult.HasError && isRedeemedResult.Error.Code != Errors.RequestError) // has error
                    return isRedeemedResult;
  
                if (!isRedeemedResult.HasError && isRedeemedResult.Value != null) // has secret
                    return isRedeemedResult;

                await Task.Delay(TimeSpan.FromSeconds(attemptIntervalInSec), cancellationToken)
                    .ConfigureAwait(false);
            }

            return new Error(Errors.MaxAttemptsCountReached, "Max attempts count reached for redeem check");
        }

        public static Task StartSwapRedeemedControlAsync(
            Swap swap,
            Currency currency,
            DateTime refundTimeUtc,
            TimeSpan interval,
            bool cancelOnlyIfRefundTimeReached = true,
            Action<Swap, byte[], CancellationToken> redeemedHandler = null,
            Action<Swap, DateTime, CancellationToken> canceledHandler = null,
            CancellationToken cancellationToken = default)
        {
            return Task.Run(async () =>
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    var isRedeemedResult = await IsRedeemedAsync(
                            swap: swap,
                            currency: currency,
                            cancellationToken: cancellationToken)
                        .ConfigureAwait(false);

                    if (isRedeemedResult.HasError && isRedeemedResult.Error.Code != Errors.RequestError) // has error
                    {
                        canceledHandler?.Invoke(swap, refundTimeUtc, cancellationToken);
                        break;
                    }
                    else if (!isRedeemedResult.HasError && isRedeemedResult.Value != null) // has secret
                    {
                        redeemedHandler?.Invoke(swap, isRedeemedResult.Value, cancellationToken);
                        break;
                    }

                    if (!cancelOnlyIfRefundTimeReached || DateTime.UtcNow >= refundTimeUtc)
                    {
                        canceledHandler?.Invoke(swap, refundTimeUtc, cancellationToken);
                        break;
                    }

                    await Task.Delay(interval, cancellationToken)
                        .ConfigureAwait(false);
                }
            }, cancellationToken);
        }
    }
}