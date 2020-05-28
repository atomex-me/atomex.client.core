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
    public static class EthereumSwapRefundedHelper
    {
        public static async Task<Result<bool>> IsRefundedAsync(
            Swap swap,
            Currency currency,
            CancellationToken cancellationToken = default)
        {
            try
            {
                Log.Debug("Ethereum: check refund event");

                var ethereum = (Atomex.Ethereum)currency;

                var api = new EtherScanApi(ethereum);

                var refundEventsResult = await api
                    .GetContractEventsAsync(
                        address: ethereum.SwapContractAddress,
                        fromBlock: ethereum.SwapContractBlockNumber,
                        toBlock: ulong.MaxValue,
                        topic0: EventSignatureExtractor.GetSignatureHash<RefundedEventDTO>(),
                        topic1: "0x" + swap.SecretHash.ToHexString(),
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                if (refundEventsResult == null)
                    return new Error(Errors.RequestError, $"Connection error while getting contract {ethereum.SwapContractAddress} refund event");

                if (refundEventsResult.HasError)
                    return refundEventsResult.Error;

                var events = refundEventsResult.Value?.ToList();

                if (events == null || !events.Any())
                    return false;

                Log.Debug("Refund event received for swap {@swap}", swap.Id);

                return true;
            }
            catch (Exception e)
            {
                Log.Error("Ethereum refund control task error");

                return new Error(Errors.InternalError, e.Message);
            }
        }

        public static async Task<Result<bool>> IsRefundedAsync(
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
    }
}