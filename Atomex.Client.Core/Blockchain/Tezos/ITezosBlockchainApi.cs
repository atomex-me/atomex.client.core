using System;
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
            DateTimeOffset? fromTimeStamp = null,
            int? fromLevel = null,
            CancellationToken cancellationToken = default);

        Task<Result<IEnumerable<IBlockchainTransaction>>> TryGetTransactionsAsync(
            string address,
            DateTimeOffset? fromTimeStamp = null,
            int? fromLevel = null,
            int attempts = 10,
            int attemptsIntervalMs = 1000,
            CancellationToken cancellationToken = default);

        Task<Result<bool>> IsRevealedAsync(
            string address,
            CancellationToken cancellationToken = default);

        Task<Result<IEnumerable<TezosTransaction>>> GetTransactionsAsync(
            string from,
            string to,
            string parameters,
            CancellationToken cancellationToken = default);

        Task<Result<IEnumerable<TezosTransaction>>> TryGetTransactionsAsync(
            string from,
            string to,
            string parameters,
            int attempts = 10,
            int attemptsIntervalMs = 1000,
            CancellationToken cancellationToken = default);
    }
}