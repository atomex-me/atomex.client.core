using System.Threading.Tasks;
using Atomix.Core.Entities;

namespace Atomix.Swaps.Abstract
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