using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Serilog;

using Atomex.Blockchain.Ethereum;
using Atomex.Blockchain.Ethereum.Erc20.Dto.Swaps.V1;
using Atomex.Common;
using Atomex.Core;
using Atomex.EthereumTokens;

namespace Atomex.Swaps.Ethereum.Erc20.Helpers
{
    public static class Erc20SwapRefundedHelper
    {
        public static async Task<Result<bool>> IsRefundedAsync(
            Swap swap,
            Erc20Config erc20Config,
            CancellationToken cancellationToken = default)
        {
            try
            {
                Log.Debug("Ethereum: check refund event");

                var api = erc20Config.GetEtherScanApi();

                var (events, error) = await api.GetContractEventsAsync(
                        address: erc20Config.SwapContractAddress,
                        fromBlock: erc20Config.SwapContractBlockNumber,
                        toBlock: ulong.MaxValue,
                        topic0: EventSignatureExtractor.GetSignatureHash<Erc20RefundedEventDTO>(),
                        topic1: "0x" + swap.SecretHash.ToHexString(),
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                if (error != null)
                    return error;

                if (events == null || !events.Any())
                    return false;

                Log.Debug("Refund event received for swap {@swap}", swap.Id);

                return true;
            }
            catch (Exception e)
            {
                Log.Error(e, "Ethereum refund control task error");

                return new Error(Errors.InternalError, e.Message);
            }
        }

        public static async Task<Result<bool>> IsRefundedAsync(
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

                var (isRefunded, error) = await IsRefundedAsync(
                        swap: swap,
                        erc20Config: erc20Config,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                if (error != null) // has error
                {
                    if (error.Value.Code != Errors.RequestError) // ignore connection errors
                        return error;
                }
                else return isRefunded;

                await Task.Delay(TimeSpan.FromSeconds(attemptIntervalInSec), cancellationToken)
                    .ConfigureAwait(false);
            }

            return new Error(Errors.MaxAttemptsCountReached, "Max attempts count reached for refund check");
        }
    }
}