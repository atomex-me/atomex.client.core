using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Atomex.Client.V1.Entities;

namespace Atomex.Client.Abstract
{
    public interface IExchangeDataRepository
    {
        #region Orders

        Task<bool> UpsertOrderAsync(
            Order order,
            CancellationToken cancellationToken = default);

        Task<Order> GetOrderByIdAsync(
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
            CancellationToken cancellationToken = default);

        #endregion Swaps
    }
}