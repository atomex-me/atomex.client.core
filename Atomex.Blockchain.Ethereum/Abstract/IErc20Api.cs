using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;

using Atomex.Blockchain.Ethereum.Erc20;
using Atomex.Common;

namespace Atomex.Blockchain.Ethereum.Abstract
{
    public interface IErc20Api
    {
        Task<(BigInteger balance, Error error)> GetErc20BalanceAsync(
            string address,
            string token,
            CancellationToken cancellationToken = default);

        Task<(IEnumerable<Erc20Transaction> txs, Error error)> GetErc20TransactionsAsync(
            string address,
            string token,
            DateTimeOffset fromTimeStamp,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets transactions count
        /// </summary>
        /// <param name="address">Address</param>
        /// <param name="pending">Include pending transactions</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Counter if success, otherwise error</returns>
        Task<(long? count, Error error)> GetTransactionsCountAsync(
            string address,
            bool pending = false,
            CancellationToken cancellationToken = default);
    }
}