using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Atomex.Common;

namespace Atomex.Blockchain.Tezos
{
    public interface ITezosBlockchainApi
    {
        Task<Result<IEnumerable<TezosTransaction>>> GetTransactionsAsync(
            string address,
            DateTimeOffset? fromTimeStamp = null,
            int? fromLevel = null,
            CancellationToken cancellationToken = default);

        Task<Result<bool>> IsRevealedAsync(
            string address,
            CancellationToken cancellationToken = default);

        Task<Result<IEnumerable<TezosTransaction>>> GetTransactionsAsync(
            string from,
            string to,
            string parameters,
            CancellationToken cancellationToken = default);
    }
}