using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Atomix.Swaps.Abstract
{
    public interface ISwapRepository
    {
        Task<bool> AddSwapAsync(ISwap swap);
        Task<bool> UpdateSwapAsync(ISwap swap);
        Task<bool> RemoveSwapAsync(ISwap swap);
        
        Task<ISwap> GetSwapByIdAsync(Guid id);
        Task<IEnumerable<ISwap>> GetSwapsAsync();
    }
}