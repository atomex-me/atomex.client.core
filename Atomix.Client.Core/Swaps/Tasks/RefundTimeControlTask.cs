using System;
using System.Threading.Tasks;
using Atomix.Blockchain.Abstract;
using Serilog;

namespace Atomix.Swaps.Tasks
{
    public class RefundTimeControlTask : BlockchainTask
    {
        public DateTime RefundTimeUtc { get; set; }

        public override Task<bool> CheckCompletion()
        {
            var refundTimeReached = DateTime.UtcNow >= RefundTimeUtc;

            Log.Debug("Refund time check for swap {@swapId}", Swap.Id);

            if (refundTimeReached)
                CompleteHandler?.Invoke(this);

            return Task.FromResult(refundTimeReached);
        }
    }
}