using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;

using Atomex.Blockchain.Ethereum.Erc20.Messages;
using Atomex.Common;

namespace Atomex.Blockchain.Ethereum.Erc20.Abstract
{
    public interface IErc20Api
    {
        Task<Result<BigInteger>> GetErc20BalanceAsync(
            string address,
            string token,
            CancellationToken cancellationToken = default);

        Task<Result<IEnumerable<Erc20Transaction>>> GetErc20TransactionsAsync(
            string address,
            string token,
            DateTimeOffset fromTimeStamp,
            CancellationToken cancellationToken = default);

        Task<Result<BigInteger>> GetErc20AllowanceAsync(
            string tokenAddress,
            Erc20AllowanceMessage allowanceMessage,
            CancellationToken cancellationToken = default);

        Task<Result<BigInteger>> GetErc20TotalSupplyAsync(
            string tokenContractAddress,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets transactions count
        /// </summary>
        /// <param name="address">Address</param>
        /// <param name="pending">Include pending transactions</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Counter if success, otherwise error</returns>
        Task<Result<BigInteger>> GetTransactionsCountAsync(
            string address,
            bool pending = false,
            CancellationToken cancellationToken = default);
    }
}