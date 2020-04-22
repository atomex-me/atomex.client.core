using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Atomex.Blockchain.Abstract;
using Atomex.Common;

namespace Atomex.Blockchain.Ethereum
{
    public interface IEthereumBlockchainApi
    {
        Task<Result<BigInteger>> GetTransactionCountAsync(
            string address,
            CancellationToken cancellationToken = default);

        Task<Result<BigInteger>> TryGetTransactionCountAsync(
            string address,
            int attempts = 3,
            int attemptsIntervalMs = 1000,
            CancellationToken cancellationToken = default);

        Task<Result<IEnumerable<IBlockchainTransaction>>> GetTransactionsAsync(
            string address,
            CancellationToken cancellationToken = default);

        Task<Result<IEnumerable<IBlockchainTransaction>>> TryGetTransactionsAsync(
            string address,
            int attempts = 3,
            int attemptsIntervalMs = 1000,
            CancellationToken cancellationToken = default);
    }
}