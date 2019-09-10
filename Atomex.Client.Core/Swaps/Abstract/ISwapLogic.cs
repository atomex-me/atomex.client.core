using System.Threading.Tasks;
using Atomex.Core.Entities;

namespace Atomex.Swaps.Abstract
{
    public interface ISwapLogic
    {
        /// <summary>
        /// Handles swap
        /// </summary>
        /// <param name="swap">Swap</param>
        /// <returns></returns>
        Task HandleSwap(ClientSwap swap);
    }
}