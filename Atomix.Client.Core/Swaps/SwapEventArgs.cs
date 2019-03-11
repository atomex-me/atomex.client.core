using System;
using Atomix.Swaps.Abstract;

namespace Atomix.Swaps
{
    public class SwapEventArgs : EventArgs
    {
        public ISwap Swap { get; }

        public SwapEventArgs()
            : this(null)
        {
        }

        public SwapEventArgs(ISwap swap)
        {
            Swap = swap;
        }
    }
}