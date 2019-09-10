using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Atomex.Blockchain.Abstract
{
    public interface IBlockchainApi
    {
        /// <summary>
        /// Get balance for <paramref name="address"/>
        /// </summary>
        /// <param name="address">Address</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Balance</returns>
        Task<decimal> GetBalanceAsync(
            string address,
            CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>
        /// Gets transaction with <paramref name="txId"/>
        /// </summary>
        /// <param name="txId">Transaction id</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Transaction if success, otherwise null</returns>
        Task<IBlockchainTransaction> GetTransactionAsync(
            string txId,
            CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>
        /// Gets all transactions (including internal) with <paramref name="txId"/>
        /// </summary>
        /// <param name="txId">Transaction id</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Transactions if success, otherwise null</returns>
        Task<IEnumerable<IBlockchainTransaction>> GetTransactionsByIdAsync(
            string txId,
            CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>
        /// Broadcast <paramref name="transaction"/> to network
        /// </summary>
        /// <param name="transaction">Transaction</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Transaction id if success, otherwise null</returns>
        Task<string> BroadcastAsync(
            IBlockchainTransaction transaction,
            CancellationToken cancellationToken = default(CancellationToken));
    }
}