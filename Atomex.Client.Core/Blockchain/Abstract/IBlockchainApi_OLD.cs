using System.Threading;
using System.Threading.Tasks;
using Atomex.Common;

namespace Atomex.Blockchain.Abstract
{
    public interface IBlockchainApi_OLD
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

        Task<Result<decimal>> TryGetBalanceAsync(
            string address,
            int attempts = 3,
            int attemptsIntervalMs = 1000,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets transaction with <paramref name="txId"/>
        /// </summary>
        /// <param name="txId">Transaction id</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Transaction if success, otherwise error</returns>
        Task<Result<IBlockchainTransaction_OLD>> GetTransactionAsync(
            string txId,
            CancellationToken cancellationToken = default);

        Task<Result<IBlockchainTransaction_OLD>> TryGetTransactionAsync(
            string txId,
            int attempts = 3,
            int attemptsIntervalMs = 1000,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Broadcast <paramref name="transaction"/> to network
        /// </summary>
        /// <param name="transaction">Transaction</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Transaction id if success, otherwise error</returns>
        Task<Result<string>> BroadcastAsync(
            IBlockchainTransaction_OLD transaction,
            CancellationToken cancellationToken = default);

        Task<Result<string>> TryBroadcastAsync(
            IBlockchainTransaction_OLD transaction,
            int attempts = 3,
            int attemptsIntervalMs = 1000,
            CancellationToken cancellationToken = default);
    }
}