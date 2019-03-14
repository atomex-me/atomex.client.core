using System;
using Atomix.Swaps.Abstract;

namespace Atomix.Swaps
{
    public class SwapEventArgs : EventArgs
    {
        public ISwapState Swap { get; }

        public SwapEventArgs()
            : this(null)
        {
        }

        public SwapEventArgs(ISwapState swap)
        {
            Swap = swap;
        }
    }
}