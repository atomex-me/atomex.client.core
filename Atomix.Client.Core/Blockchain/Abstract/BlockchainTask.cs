using Atomix.Common;
using Atomix.Core.Entities;

namespace Atomix.Blockchain.Abstract
{
    public abstract class BlockchainTask : BackgroundTask
    {
        public Currency Currency { get; set; }
        public ClientSwap Swap { get; set; }
    }
}