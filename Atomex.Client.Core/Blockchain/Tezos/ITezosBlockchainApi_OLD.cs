using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Atomex.Blockchain.Abstract;
using Atomex.Common;

namespace Atomex.Blockchain.Tezos
{
    public interface ITezosBlockchainApi_OLD
    {
        Task<Result<IEnumerable<IBlockchainTransaction_OLD>>> GetTransactionsAsync(
            string address,
            CancellationToken cancellationToken = default);

        Task<Result<IEnumerable<IBlockchainTransaction_OLD>>> TryGetTransactionsAsync(
            string address,
            int attempts = 10,
            int attemptsIntervalMs = 1000,
            CancellationToken cancellationToken = default);

        Task<Result<bool>> IsRevealedAsync(
            string address,
            CancellationToken cancellationToken = default);

        Task<Result<IEnumerable<TezosTransaction_OLD>>> GetTransactionsAsync(
            string from,
            string to,
            string parameters,
            CancellationToken cancellationToken = default);

        Task<Result<IEnumerable<TezosTransaction_OLD>>> TryGetTransactionsAsync(
            string from,
            string to,
            string parameters,
            int attempts = 10,
            int attemptsIntervalMs = 1000,
            CancellationToken cancellationToken = default);
    }
}