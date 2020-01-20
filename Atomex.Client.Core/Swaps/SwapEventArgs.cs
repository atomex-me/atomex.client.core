using System;
using Atomex.Core;

namespace Atomex.Swaps
{
    public class SwapEventArgs : EventArgs
    {
        public Swap Swap { get; }
        public SwapStateFlags ChangedFlag { get; }

        public SwapEventArgs(Swap swap)
            : this(swap, SwapStateFlags.Empty)
        {
        }

        public SwapEventArgs(Swap swap, SwapStateFlags changedFlag)
        {
            Swap = swap;
            ChangedFlag = changedFlag;
        }
    }
}