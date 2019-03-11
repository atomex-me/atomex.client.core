using Atomix.Common;
using Atomix.Core.Entities;
using Atomix.Swaps;

namespace Atomix.Blockchain.Abstract
{
    public abstract class BlockchainTask : BackgroundTask
    {
        public Currency Currency { get; set; }
        public Swap Swap { get; set; }
    }
}