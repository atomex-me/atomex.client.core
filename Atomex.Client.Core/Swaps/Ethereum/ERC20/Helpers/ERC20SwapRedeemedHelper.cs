using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Serilog;

using Atomex.Blockchain.Ethereum;
using Atomex.Blockchain.Ethereum.ERC20;
using Atomex.Common;
using Atomex.Core;

namespace Atomex.Swaps.Ethereum.ERC20.Helpers
{

    public static class ERC20SwapRedeemedHelper
    {
        public static async Task<Result<byte[]>> IsRedeemedAsync(
            Swap swap,
            CurrencyConfig currency,
            CancellationToken cancellationToken = default)
        {
            try
            {
                Log.Debug("Ethereum ERC20: check redeem event");

                var erc20 = (Atomex.EthereumTokens.Erc20Config)currency;

                var api = new EtherScanApi(erc20);

                var redeemEventsResult = await api.GetContractEventsAsync(
                        address: erc20.SwapContractAddress,
                        fromBlock: erc20.SwapContractBlockNumber,
                        toBlock: ulong.MaxValue,
                        topic0: EventSignatureExtractor.GetSignatureHash<ERC20RedeemedEventDTO>(),
                        topic1: "0x" + swap.SecretHash.ToHexString(),
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                if (redeemEventsResult == null)
                    return new Result<byte[]>(new Error(Errors.RequestError, $"Connection error while trying to get contract {erc20.SwapContractAddress} redeem events"));

                if (redeemEventsResult.HasError)
                    return new Result<byte[]>(redeemEventsResult.Error);

                var events = redeemEventsResult.Value?.ToList();

                if (events == null || !events.Any())
                    return new Result<byte[]>((byte[])null);

                var secret = events.Last().ParseRedeemedEvent().Secret;

                Log.Debug("Redeem event received with secret {@secret}", Convert.ToBase64String(secret));

                return new Result<byte[]>(secret);
            }
            catch (Exception e)
            {
                Log.Error(e, "Ethereum ERC20 redeem control task error");

                return new Result<byte[]>(new Error(Errors.InternalError, e.Message));
            }
        }

        public static async Task<Result<byte[]>> IsRedeemedAsync(
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

                var isRedeemedResult = await IsRedeemedAsync(
                        swap: swap,
                        currency: currency,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                if (isRedeemedResult.HasError) // has error
                {
                    if (isRedeemedResult.Error.Code != Errors.RequestError) // ignore connection errors
                        return isRedeemedResult;
                }
                else if (isRedeemedResult.Value != null) // has secret
                {
                    return isRedeemedResult;
                }

                await Task.Delay(TimeSpan.FromSeconds(attemptIntervalInSec), cancellationToken)
                    .ConfigureAwait(false);
            }

            return new Result<byte[]>(new Error(Errors.MaxAttemptsCountReached, "Max attempts count reached for redeem check"));
        }

        public static Task StartSwapRedeemedControlAsync(
            Swap swap,
            CurrencyConfig currency,
            DateTime refundTimeUtc,
            TimeSpan interval,
            Func<Swap, byte[], CancellationToken, Task> redeemedHandler,
            Func<Swap, DateTime, CancellationToken, Task> canceledHandler,
            CancellationToken cancellationToken = default)
        {
            return Task.Run(async () =>
            {
                try
                {
                    while (!cancellationToken.IsCancellationRequested)
                    {
                        var isRedeemedResult = await IsRedeemedAsync(
                                swap: swap,
                                currency: currency,
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
                }
                catch (OperationCanceledException)
                {
                    Log.Debug("StartSwapRedeemedControlAsync canceled");
                }
                catch (Exception e)
                {
                    Log.Error(e, "StartSwapRedeemedControlAsync error");
                }

            }, cancellationToken);
        }
    }
}