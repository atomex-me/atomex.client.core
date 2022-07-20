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
            Error = error ?? throw new ArgumentNullException(nameof(error));
            SwapId = swapId;
        }
    }
}