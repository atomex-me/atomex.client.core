using System;
using System.Threading;
using System.Threading.Tasks;

using Atomex.Core;

namespace Atomex.Swaps.Abstract
{
    public interface ISwapManager
    {
        event EventHandler<SwapEventArgs> SwapUpdated;

        void Start(CancellationToken cancellationToken = default);
        void Stop();
        Task<Error> HandleSwapAsync(
            Swap receivedSwap,
            CancellationToken cancellationToken = default);
    }
}