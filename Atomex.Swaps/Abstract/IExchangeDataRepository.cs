using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Atomex.Swaps.Entities;

namespace Atomex.Client.Abstract
{
    public interface IExchangeDataRepository
    {
        #region Orders

        Task<bool> UpsertOrderAsync(
            Order order,
            CancellationToken cancellationToken = default);

        Task<Order> GetOrderByClientIdAsync(
            string clientOrderId,
            CancellationToken cancellationToken = default);

        Task<Order> GetOrderByIdAsync(
            long id,
            CancellationToken cancellationToken = default);

        #endregion Orders

        #region Swaps

        Task<bool> AddSwapAsync(
            Swap swap,
            CancellationToken cancellationToken = default);

        Task<bool> UpdateSwapAsync(
            Swap swap,
            CancellationToken cancellationToken = default);

        Task<Swap> GetSwapByIdAsync(
            long id,
            CancellationToken cancellationToken = default);

        Task<IEnumerable<Swap>> GetSwapsAsync(
            int offset = 0,
            int limit = int.MaxValue,
            CancellationToken cancellationToken = default);

        Task<IEnumerable<Swap>> GetActiveSwapsAsync(
            int offset = 0,
            int limit = int.MaxValue,
            CancellationToken cancellationToken = default);

        #endregion Swaps
    }
}