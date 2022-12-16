using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Serilog;

using Atomex.Blockchain.Ethereum;
using Atomex.Common;
using Atomex.Core;
using Atomex.Blockchain.Ethereum.Erc20.Dto.Swaps.V1;

namespace Atomex.Swaps.Ethereum.Erc20.Helpers
{

    public static class Erc20SwapRedeemedHelper
    {
        public static async Task<Result<byte[]>> IsRedeemedAsync(
            Swap swap,
            CurrencyConfig currency,
            CancellationToken cancellationToken = default)
        {
            try
            {
                Log.Debug("Ethereum ERC20: check redeem event");

                var erc20 = (EthereumTokens.Erc20Config)currency;

                var api = new EtherScanApi(erc20.Name, erc20.BlockchainApiBaseUri);

                var (events, error) = await api.GetContractEventsAsync(
                        address: erc20.SwapContractAddress,
                        fromBlock: erc20.SwapContractBlockNumber,
                        toBlock: ulong.MaxValue,
                        topic0: EventSignatureExtractor.GetSignatureHash<Erc20RedeemedEventDTO>(),
                        topic1: "0x" + swap.SecretHash.ToHexString(),
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                if (error != null)
                    return error;

                if (events == null || !events.Any())
                    return new Result<byte[]> { Value = null };

                var secret = events.Last().ParseRedeemedEvent().Secret;

                Log.Debug("Redeem event received with secret {@secret}", Convert.ToBase64String(secret));

                return new Result<byte[]> { Value = secret };
            }
            catch (Exception e)
            {
                Log.Error(e, "Ethereum ERC20 redeem control task error");

                return new Error(Errors.InternalError, e.Message);
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

                var (secret, error) = await IsRedeemedAsync(
                        swap: swap,
                        currency: currency,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                if (error != null) // has error
                {
                    if (error.Value.Code != Errors.RequestError) // ignore connection errors
                        return error;
                }
                else if (secret != null) // has secret
                {
                    return secret;
                }

                await Task.Delay(TimeSpan.FromSeconds(attemptIntervalInSec), cancellationToken)
                    .ConfigureAwait(false);
            }

            return new Error(Errors.MaxAttemptsCountReached, "Max attempts count reached for redeem check");
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
            Log.Debug("StartSwapRedeemedControlAsync for {@Currency} swap with id {@swapId} started", currency.Name, swap.Id);

            return Task.Run(async () =>
            {
                try
                {
                    while (!cancellationToken.IsCancellationRequested)
                    {
                        var (secret, error) = await IsRedeemedAsync(
                                swap: swap,
                                currency: currency,
                                cancellationToken: cancellationToken)
                            .ConfigureAwait(false);

                        if (error != null)
                        {
                            Log.Error("{@currency} IsRedeemedAsync error for swap {@swap}. Code: {@code}. Message: {@desc}",
                                currency.Name,
                                swap.Id,
                                error.Value.Code,
                                error.Value.Message);
                        }
                        else if (error == null && secret != null) // has secret
                        {
                            await redeemedHandler
                                .Invoke(swap, secret, cancellationToken)
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
    }
}