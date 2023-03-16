using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Serilog;

using Atomex.Blockchain.Ethereum;
using Atomex.Blockchain.Ethereum.EtherScan;
using Atomex.Blockchain.Ethereum.Erc20.Dto.Swaps.V1;
using Atomex.Common;
using Atomex.Core;
using Atomex.EthereumTokens;

namespace Atomex.Swaps.Ethereum.Erc20.Helpers
{
    public static class Erc20SwapRedeemedHelper
    {
        public static async Task<Result<byte[]>> IsRedeemedAsync(
            Swap swap,
            Erc20Config erc20Config,
            CancellationToken cancellationToken = default)
        {
            try
            {
                Log.Debug("Ethereum ERC20: check redeem event");

                var api = erc20Config.GetEtherScanApi();

                var (events, error) = await api.GetContractEventsAsync(
                        address: erc20Config.SwapContractAddress,
                        fromBlock: erc20Config.SwapContractBlockNumber,
                        toBlock: ulong.MaxValue,
                        topic0: EventSignatureExtractor.GetSignatureHash<Erc20RedeemedEventDTO>(),
                        topic1: "0x" + swap.SecretHash.ToHexString(),
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                if (error != null)
                    return error;

                if (events == null || !events.Any())
                    return new Result<byte[]> { Value = null };

                var secret = events
                    .Last()
                    .ParseErc20RedeemedEvent()
                    .Secret;

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
            Erc20Config erc20Config,
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
                        erc20Config: erc20Config,
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

        public static async Task StartSwapRedeemedControlAsync(
            Swap swap,
            Erc20Config erc20Config,
            DateTime refundTimeUtc,
            TimeSpan interval,
            Func<Swap, byte[], CancellationToken, Task> redeemedHandler,
            Func<Swap, DateTime, CancellationToken, Task> canceledHandler,
            CancellationToken cancellationToken = default)
        {
            Log.Debug("StartSwapRedeemedControlAsync for {@Currency} swap with id {@swapId} started", erc20Config.Name, swap.Id);

            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    var (secret, error) = await IsRedeemedAsync(
                            swap: swap,
                            erc20Config: erc20Config,
                            cancellationToken: cancellationToken)
                        .ConfigureAwait(false);

                    if (error != null)
                    {
                        Log.Error("{@currency} IsRedeemedAsync error for swap {@swap}. Code: {@code}. Message: {@desc}",
                            erc20Config.Name,
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
                    erc20Config.Name,
                    swap.Id,
                    cancellationToken.IsCancellationRequested ? "canceled" : "completed");
            }
            catch (OperationCanceledException)
            {
                Log.Debug("StartSwapRedeemedControlAsync for {@Currency} swap with id {@swapId} canceled",
                    erc20Config.Name,
                    swap.Id);
            }
            catch (Exception e)
            {
                Log.Error(e, "StartSwapRedeemedControlAsync for {@Currency} swap with id {@swapId} error",
                    erc20Config.Name,
                    swap.Id);
            }
        }
    }
}