using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Atomex.Blockchain.Abstract;
using Atomex.Common;

namespace Atomex.Blockchain.Tezos
{
    public interface ITezosBlockchainApi
    {
        Task<Result<IEnumerable<IBlockchainTransaction>>> GetTransactionsAsync(
            string address,
            CancellationToken cancellationToken = default);

        Task<Result<IEnumerable<IBlockchainTransaction>>> TryGetTransactionsAsync(
            string address,
            int attempts = 10,
            int attemptsIntervalMs = 1000,
            CancellationToken cancellationToken = default);

        Task<Result<bool>> IsActiveAddress(
            string address,
            CancellationToken cancellationToken = default);
    }
}