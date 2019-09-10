using Atomex.Common;
using Atomex.Core.Entities;

namespace Atomex.Blockchain.Abstract
{
    public abstract class BlockchainTask : BackgroundTask
    {
        public Currency Currency { get; set; }
        public ClientSwap Swap { get; set; }
    }
}