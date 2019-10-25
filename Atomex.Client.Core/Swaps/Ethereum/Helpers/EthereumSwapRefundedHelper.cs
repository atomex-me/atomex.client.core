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
    public static class EthereumSwapRefundedHelper
    {
        public static async Task<Result<bool>> IsRefundedAsync(
            ClientSwap swap,
            Currency currency,
            CancellationToken cancellationToken = default)
        {
            try
            {
                Log.Debug("Ethereum: check refund event");

                var ethereum = (Atomex.Ethereum)currency;

                var api = new EtherScanApi(ethereum);

                var refundEventsResult = await api.GetContractEventsAsync(
                        address: ethereum.SwapContractAddress,
                        fromBlock: ethereum.SwapContractBlockNumber,
                        toBlock: ulong.MaxValue,
                        topic0: EventSignatureExtractor.GetSignatureHash<RefundedEventDTO>(),
                        topic1: "0x" + swap.SecretHash.ToHexString(),
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                if (refundEventsResult.HasError)
                    return new Result<bool>(refundEventsResult.Error);

                var events = refundEventsResult.Value?.ToList();

                if (events == null || !events.Any())
                    return new Result<bool>(false);

                Log.Debug("Refund event received for swap {@swap}", swap.Id);

                return new Result<bool>(true);
            }
            catch (Exception e)
            {
                Log.Error(e, "Ethereum refund control task error");

                return new Result<bool>(new Error(Errors.InternalError, e.Message));
            }
        }

        //public static Task StartSwapRefundedControlAsync(
        //    ClientSwap swap,
        //    Currency currency,
        //    TimeSpan interval,
        //    Action<ClientSwap, CancellationToken> refundedHandler = null,
        //    Action<ClientSwap, CancellationToken> canceledHandler = null,
        //    CancellationToken cancellationToken = default)
        //{
        //    return Task.Run(async () =>
        //    {
        //        while (!cancellationToken.IsCancellationRequested)
        //        {
        //            var isRefundedResult = await IsRefundedAsync(
        //                    swap: swap,
        //                    currency: currency,
        //                    cancellationToken: cancellationToken)
        //                .ConfigureAwait(false);

        //            if (isRefundedResult.HasError) // has error
        //            {
        //                canceledHandler?.Invoke(swap, cancellationToken);
        //                break;
        //            }

        //            if (isRefundedResult.Value)
        //            {
        //                refundedHandler?.Invoke(swap, cancellationToken);
        //                break;
        //            }

        //            await Task.Delay(interval, cancellationToken)
        //                .ConfigureAwait(false);
        //        }
        //    }, cancellationToken);
        //}
    }
}