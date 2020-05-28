using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Atomex.Blockchain.Ethereum;
using Atomex.Common;
using Atomex.Core;
using Serilog;

namespace Atomex.Swaps.Ethereum.Helpers
{
    public static class EthereumSwapRedeemedHelper
    {
        public static async Task<Result<byte[]>> IsRedeemedAsync(
            Swap swap,
            Currency currency,
            CancellationToken cancellationToken = default)
        {
            try
            {
                Log.Debug("Ethereum: check redeem event");

                var ethereum = (Atomex.Ethereum)currency;
                  
                var api = new EtherScanApi(ethereum);

                var redeemEventsResult = await api
                    .GetContractEventsAsync(
                        address: ethereum.SwapContractAddress,
                        fromBlock: ethereum.SwapContractBlockNumber,
                        toBlock: ulong.MaxValue,
                        topic0: EventSignatureExtractor.GetSignatureHash<RedeemedEventDTO>(),
                        topic1: "0x" + swap.SecretHash.ToHexString(),
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                if (redeemEventsResult == null)
                    return new Error(Errors.RequestError, $"Connection error while getting contract {ethereum.SwapContractAddress} redeem events");

                if (redeemEventsResult.HasError)
                    return redeemEventsResult.Error;

                var events = redeemEventsResult.Value?.ToList();

                if (events == null || !events.Any())
                    return (byte[])null;

                var secret = events.First().ParseRedeemedEvent().Secret;

                Log.Debug("Redeem event received with secret {@secret}", Convert.ToBase64String(secret));

                return secret;
            }
            catch (Exception e)
            {
                Log.Error("Ethereum redeem control task error");

                return new Error(Errors.InternalError, e.Message);
            }
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

                if (!isRedeemedResult.HasError)
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