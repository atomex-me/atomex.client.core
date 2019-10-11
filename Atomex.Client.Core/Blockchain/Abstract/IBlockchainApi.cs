using System.Threading;
using System.Threading.Tasks;
using Atomex.Common;

namespace Atomex.Blockchain.Abstract
{
    public interface IBlockchainApi
    {
        /// <summary>
        /// Get balance for <paramref name="address"/>
        /// </summary>
        /// <param name="address">Address</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Balance if success, otherwise error</returns>
        Task<Result<decimal>> GetBalanceAsync(
            string address,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets transaction with <paramref name="txId"/>
        /// </summary>
        /// <param name="txId">Transaction id</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Transaction if success, otherwise error</returns>
        Task<Result<IBlockchainTransaction>> GetTransactionAsync(
            string txId,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Broadcast <paramref name="transaction"/> to network
        /// </summary>
        /// <param name="transaction">Transaction</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Transaction id if success, otherwise error</returns>
        Task<Result<string>> BroadcastAsync(
            IBlockchainTransaction transaction,
            CancellationToken cancellationToken = default);
    }
}