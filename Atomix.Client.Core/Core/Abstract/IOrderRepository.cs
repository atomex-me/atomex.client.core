using System.Threading.Tasks;
using Atomix.Core.Entities;

namespace Atomix.Core.Abstract
{
    public interface IOrderRepository
    {
        Task<bool> AddOrderAsync(Order order);
    }
}