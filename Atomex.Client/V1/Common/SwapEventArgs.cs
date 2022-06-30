using System;

using Atomex.Client.V1.Entities;

namespace Atomex.Client.V1.Common
{
    public class SwapEventArgs : EventArgs
    {
        public Swap Swap { get; }

        public SwapEventArgs(Swap swap)
        {
            Swap = swap;
        }
    }
}