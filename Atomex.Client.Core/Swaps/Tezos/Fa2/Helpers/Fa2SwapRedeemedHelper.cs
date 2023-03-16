using System;
using System.Threading;
using System.Threading.Tasks;

using Serilog;

using Atomex.Blockchain.Tezos.Tzkt;
using Atomex.Blockchain.Tezos.Tzkt.Swaps.V1;
using Atomex.Common;
using Atomex.Core;
using Atomex.TezosTokens;

namespace Atomex.Swaps.Tezos.Fa2.Helpers
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
                var secretHash = swap.SecretHash.ToHexString();

                var api = new TzktApi(tezos.GetTzktSettings());

                var (ops, error) = await api
                    .FindRedeemsAsync(
                        secretHash: secretHash,
                        contractAddress: contractAddress,
                        timeStamp: (ulong)swap.TimeStamp.ToUnixTimeSeconds(),
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                if (error != null)
                {
                    Log.Error("Error while get transactions from contract {@contract}. Code: {@code}. Message: {@desc}",
                        contractAddress,
                        error.Value.Code,
                        error.Value.Message);

                    return error;
                }

                foreach (var op in ops)
                {
                    if (op.IsRedeem(contractAddress, secretHash, out var secret))
                    {
                        Log.Debug("Redeem event received with secret {@secret}", secret);
                        return Hex.FromString(secret);
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

                var (isRedeemed, error) = await IsRedeemedAsync(
                        swap: swap,
                        currency: currency,
                        tezos: tezos,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                if (error != null && error.Value.Code != Errors.RequestError) // has error
                    return error;

                if (error == null)
                    return isRedeemed;

                await Task.Delay(TimeSpan.FromSeconds(attemptIntervalInSec), cancellationToken)
                    .ConfigureAwait(false);
            }

            return new Error(Errors.MaxAttemptsCountReached, "Max attempts count reached for redeem check");
        }

        public static async Task StartSwapRedeemedControlAsync(
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

            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    var (secret, error) = await IsRedeemedAsync(
                            swap: swap,
                            currency: currency,
                            tezos: tezos,
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
        }
    }
}