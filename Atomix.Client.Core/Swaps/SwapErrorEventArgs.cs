using Atomix.Core;

namespace Atomix.Swaps
{
    public class SwapErrorEventArgs : ErrorEventArgs
    {
        public SwapData SwapData { get; }

        public SwapErrorEventArgs(Error error, SwapData swapData)
            : base(error)
        {
            SwapData = swapData;
        }
    }
}