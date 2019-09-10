using System;
using Atomex.Core.Entities;

namespace Atomex.Swaps
{
    public class SwapEventArgs : EventArgs
    {
        public ClientSwap Swap { get; }
        public SwapStateFlags ChangedFlag { get; }

        public SwapEventArgs(ClientSwap swap)
            : this(swap, SwapStateFlags.Empty)
        {
        }

        public SwapEventArgs(ClientSwap swap, SwapStateFlags changedFlag)
        {
            Swap = swap;
            ChangedFlag = changedFlag;
        }
    }
}