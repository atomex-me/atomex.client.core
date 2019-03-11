using System;

namespace Atomix.Swaps
{
    public class SwapDataEventArgs : EventArgs
    {
        public SwapData SwapData { get; set; }

        public SwapDataEventArgs(SwapData swapData)
        {
            SwapData = swapData;
        }
    }
}