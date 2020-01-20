using System.Threading.Tasks;
using Atomex.Core;

namespace Atomex.Swaps.Abstract
{
    public interface ISwapLogic
    {
        /// <summary>
        /// Handles swap
        /// </summary>
        /// <param name="swap">Swap</param>
        /// <returns></returns>
        Task HandleSwap(Swap swap);
    }
}