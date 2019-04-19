using System.Threading.Tasks;

namespace Atomix.Swaps.Abstract
{
    public interface ISwap
    {
        /// <summary>
        /// Initiates swap for the currency being sold
        /// </summary>
        /// <returns></returns>
        Task InitiateSwapAsync();

        /// <summary>
        /// Accepts swap for the currency being sold
        /// </summary>
        /// <returns></returns>
        Task AcceptSwapAsync();

        /// <summary>
        /// Restores swap
        /// </summary>
        /// <returns></returns>
        Task RestoreSwapAsync();

        /// <summary>
        /// Handles swap data messages
        /// </summary>
        /// <param name="swapData">Swap data</param>
        /// <returns></returns>
        Task HandleSwapData(SwapData swapData);
    }
}