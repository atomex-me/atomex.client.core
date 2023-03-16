using System.Numerics;
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
        Task<Result<BigInteger>> GetBalanceAsync(
            string address,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets transaction with <paramref name="txId"/>
        /// </summary>
        /// <param name="txId">Transaction id</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Transaction if success, otherwise error</returns>
        Task<Result<ITransaction>> GetTransactionAsync(
            string txId,
            CancellationToken cancellationToken = default);
    }
}