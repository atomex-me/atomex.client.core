using System;

using Atomex.Common;

namespace Atomex.Swaps
{
    public class SwapErrorEventArgs : EventArgs
    {
        public Error Error { get; }
        public long SwapId { get; }

        public SwapErrorEventArgs(Error error, long swapId)
        {
            Error = error;
            SwapId = swapId;
        }
    }
}