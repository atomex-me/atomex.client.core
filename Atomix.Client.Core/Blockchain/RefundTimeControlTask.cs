using System;
using System.Threading.Tasks;
using Atomix.Blockchain.Abstract;

namespace Atomix.Blockchain
{
    public class RefundTimeControlTask : BlockchainTask
    {
        public DateTime RefundTimeUtc { get; set; }

        public override Task<bool> CheckCompletion()
        {
            bool refundTimeReached = DateTime.UtcNow >= RefundTimeUtc;

            if (refundTimeReached)
                CompleteHandler?.Invoke(this);

            return Task.FromResult(refundTimeReached);
        }
    }
}