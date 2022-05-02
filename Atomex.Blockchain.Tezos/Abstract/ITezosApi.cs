using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Atomex.Blockchain.Abstract;
using Atomex.Blockchain.Tezos.Common;
using Atomex.Common;

namespace Atomex.Blockchain.Tezos.Abstract
{
    public interface ITezosApi : IBlockchainApi
    {
        /// <summary>
        /// Gets all operations by address
        /// </summary>
        /// <param name="address">Address</param>
        /// <param name="fromTimeStamp">From time</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Operations if success, otherwise error</returns>
        Task<(IEnumerable<TezosOperation> ops, Error error)> GetOperationsAsync(
            string address,
            DateTimeOffset fromTimeStamp,
            CancellationToken cancellationToken = default);

        Task<(TezosAccount account, Error error)> GetAccountAsync(
            string address,
            CancellationToken cancellationToken = default);

        Task<(string hash, Error error)> GetHeaderAsync(
            int offset = 0,
            CancellationToken cancellationToken = default);

        Task<(bool isRevealed, Error error)> IsRevealedAsync(
            string address,
            CancellationToken cancellationToken = default);

        Task<(int? counter, Error error)> GetCounterAsync(
            string address,
            CancellationToken cancellationToken = default);

        Task<(string result, Error error)> RunOperationsAsync(
            string branch,
            string chainId,
            string operations,
            CancellationToken cancellationToken = default);
    }
}