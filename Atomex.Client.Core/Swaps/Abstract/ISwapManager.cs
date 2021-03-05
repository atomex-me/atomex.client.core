using System;
using System.Threading;
using System.Threading.Tasks;

using Atomex.Core;

namespace Atomex.Swaps.Abstract
{
    public interface ISwapManager
    {
        event EventHandler<SwapEventArgs> SwapUpdated;

        Task<Error> HandleSwapAsync(
            Swap receivedSwap,
            CancellationToken cancellationToken = default);

        Task RestoreSwapsAsync(
            CancellationToken cancellationToken = default);

        Task SwapTimeoutControlAsync(
            CancellationToken cancellationToken = default);

        void Clear();
    }
}