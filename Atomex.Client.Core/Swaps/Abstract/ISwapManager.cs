using System;
using System.Threading;
using System.Threading.Tasks;
using Atomex.Core;

namespace Atomex.Swaps.Abstract
{
    public interface ISwapManager
    {
        event EventHandler<SwapEventArgs> SwapUpdated;

        Task HandleSwapAsync(Swap receivedSwap);
        Task RestoreSwapsAsync(CancellationToken cancellationToken = default);
    }
}