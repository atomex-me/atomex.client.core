using System;
using System.Threading.Tasks;

using Atomex.Common;
using Atomex.Core;

namespace Atomex.Swaps.Abstract
{
    public interface ISwapManager
    {
        event EventHandler<SwapEventArgs> SwapUpdated;
        event EventHandler<SwapErrorEventArgs> SwapError;

        /// <summary>
        /// Start swap manager services
        /// </summary>
        void Start();
        /// <summary>
        /// Stop swap manager services and all swaps handlers
        /// </summary>
        void Stop();
        /// <summary>
        /// Handle received swap
        /// </summary>
        /// <param name="receivedSwap">Received swap</param>
        /// <returns>Null if everything is ok, otherwise error</returns>
        Task<Error> HandleSwapAsync(Swap receivedSwap);
        /// <summary>
        /// Cancel swap
        /// </summary>
        /// <param name="id">Swap id</param>
        /// <returns>Null if everything is ok, otherwise error</returns>
        Task<Error> CancelSwapAsync(long id);
        /// <summary>
        /// Resume canceled swap
        /// </summary>
        /// <param name="id">Swap id</param>
        /// <returns>Null if everything is ok, otherwise error</returns>
        Task<Error> ResumeSwapAsync(long id);
        /// <summary>
        /// Restart swap handler
        /// </summary>
        /// <param name="id">Swap id</param>
        /// <returns>Null if everything is ok, otherwise error</returns>
        Task<Error> RestartSwapAsync(long id);
    }
}