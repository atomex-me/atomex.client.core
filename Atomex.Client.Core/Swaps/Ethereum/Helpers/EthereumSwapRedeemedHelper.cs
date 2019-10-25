using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Atomex.Blockchain.Ethereum;
using Atomex.Common;
using Atomex.Core;
using Atomex.Core.Entities;
using Serilog;

namespace Atomex.Swaps.Ethereum.Helpers
{

    public static class EthereumSwapRedeemedHelper
    {
        public static async Task<Result<byte[]>> IsRedeemedAsync(
            ClientSwap swap,
            Currency currency,
            CancellationToken cancellationToken = default)
        {
            try
            {
                Log.Debug("Ethereum: check redeem event");

                var ethereum = (Atomex.Ethereum)currency;

                var api = new EtherScanApi(ethereum);

                var redeemEventsResult = await api.GetContractEventsAsync(
                        address: ethereum.SwapContractAddress,
                        fromBlock: ethereum.SwapContractBlockNumber,
                        toBlock: ulong.MaxValue,
                        topic0: EventSignatureExtractor.GetSignatureHash<RedeemedEventDTO>(),
                        topic1: "0x" + swap.SecretHash.ToHexString(),
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                if (redeemEventsResult.HasError)
                    return new Result<byte[]>(redeemEventsResult.Error);

                var events = redeemEventsResult.Value?.ToList();

                if (events == null || !events.Any())
                    return new Result<byte[]>((byte[])null);

                var secret = events.First().ParseRedeemedEvent().Secret;

                Log.Debug("Redeem event received with secret {@secret}", Convert.ToBase64String(secret));

                return new Result<byte[]>(secret);
            }
            catch (Exception e)
            {
                Log.Error(e, "Ethereum redeem control task error");

                return new Result<byte[]>(new Error(Errors.InternalError, e.Message));
            }
        }

        public static Task StartSwapRedeemedControlAsync(
            ClientSwap swap,
            Currency currency,
            DateTime refundTimeUtc,
            TimeSpan interval,
            bool cancelOnlyIfRefundTimeReached = true,
            Action<ClientSwap, byte[], CancellationToken> redeemedHandler = null,
            Action<ClientSwap, DateTime, CancellationToken> canceledHandler = null,
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

                    if (isRedeemedResult.HasError) // has error
                    {
                        canceledHandler?.Invoke(swap, refundTimeUtc, cancellationToken);
                        break;
                    }

                    if (isRedeemedResult.Value != null) // has secret
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