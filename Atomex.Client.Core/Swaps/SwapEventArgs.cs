using System;
using System.Threading;
using Atomex.Core;

namespace Atomex.Swaps
{
    public class SwapEventArgs : EventArgs
    {
        public Swap Swap { get; }
        public SwapStateFlags ChangedFlag { get; }
        public CancellationToken CancellationToken{ get; }

        public SwapEventArgs(Swap swap)
            : this(swap, SwapStateFlags.Empty, default)
        {
        }

        public SwapEventArgs(Swap swap, SwapStateFlags changedFlag)
            : this(swap, changedFlag, default)
        {
        }

        public SwapEventArgs(Swap swap, CancellationToken cancellationToken)
            : this(swap, SwapStateFlags.Empty, cancellationToken)
        {
        }

        public SwapEventArgs(Swap swap, SwapStateFlags changedFlag, CancellationToken cancellationToken)
        {
            Swap = swap;
            ChangedFlag = changedFlag;
            CancellationToken = cancellationToken;
        }
    }
}