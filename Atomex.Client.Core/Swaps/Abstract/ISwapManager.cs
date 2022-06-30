using System;
using System.Threading.Tasks;
using Atomex.Common;
using Atomex.Core;

namespace Atomex.Swaps.Abstract
{
    public interface ISwapManager
    {
        event EventHandler<SwapEventArgs> SwapUpdated;

        void Start();
        void Stop();
        Task<Error> HandleSwapAsync(Swap receivedSwap);
    }
}