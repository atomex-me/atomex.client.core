using System;
using System.Threading;
using System.Threading.Tasks;
using Atomex.Core.Entities;

namespace Atomex.Swaps.Abstract
{
    public interface IClientSwapManager
    {
        event EventHandler<SwapEventArgs> SwapUpdated;

        Task HandleSwapAsync(ClientSwap receivedSwap);
        Task RestoreSwapsAsync(CancellationToken cancellationToken = default);
    }
}