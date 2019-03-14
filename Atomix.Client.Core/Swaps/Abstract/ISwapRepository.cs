using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Atomix.Swaps.Abstract
{
    public interface ISwapRepository
    {
        Task<bool> AddSwapAsync(ISwapState swap);
        Task<bool> UpdateSwapAsync(ISwapState swap);
        Task<bool> RemoveSwapAsync(ISwapState swap);
        
        Task<ISwapState> GetSwapByIdAsync(Guid id);
        Task<IEnumerable<ISwapState>> GetSwapsAsync();
    }
}