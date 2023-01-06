using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Atomex.Common;

namespace Atomex.Blockchain.Tezos.Abstract
{
    public interface ITezosApi
    {
        Task<Result<IEnumerable<TezosOperation>>> GetOperationsAsync(
            string address,
            DateTimeOffset? fromTimeStamp = null,
            CancellationToken cancellationToken = default);

        Task<Result<IEnumerable<TezosOperation>>> GetTransactionsAsync(
            string from,
            string to,
            string parameters,
            CancellationToken cancellationToken = default);

        Task<Result<bool>> IsRevealedAsync(
            string address,
            CancellationToken cancellationToken = default);
    }
}