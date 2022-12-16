using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;

using Atomex.Common;

namespace Atomex.Blockchain.Ethereum.Abstract
{
    public interface IEthereumApi : IGasPriceProvider
    {
        /// <summary>
        /// Gets all transactions by address (including internals)
        /// </summary>
        /// <param name="address">Address</param>
        /// <param name="fromTimeStamp">From time</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Transactions if success, otherwise error</returns>
        Task<Result<IEnumerable<EthereumTransaction>>> GetTransactionsAsync(
            string address,
            DateTimeOffset fromTimeStamp,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets all transactions by address (including internals)
        /// </summary>
        /// <param name="address">Address</param>
        /// <param name="fromBlock">From block</param>
        /// <param name="toBlock">To block</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Transactions if success, otherwise error</returns>
        Task<Result<IEnumerable<EthereumTransaction>>> GetTransactionsAsync(
            string address,
            ulong fromBlock = 0,
            ulong toBlock = ulong.MaxValue,
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

        /// <summary>
        /// Gets fastest gas price estimation
        /// </summary>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Gas price in the first element of the tuple if success, otherwise error in the second element of the tuple</returns>
        Task<Result<long>> GetFastGasPriceAsync(
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Generates and returns an estimate of how much gas is necessary to allow the transaction to complete.
        /// </summary>
        /// <remarks>
        /// The estimate may be significantly more than the amount of gas actually used by the transaction. To prevent abusive of the API the <paramref name="gasLimit"/> parameter can be capped at 2x or 10x the current block gas limit. It depends on API provider.
        /// </remarks>
        /// <param name="to">The address the transaction is directed to</param>
        /// <param name="from">The address the transaction is sent from (optional)</param>
        /// <param name="value">Integer of the value sent with this transaction (optional)</param>
        /// <param name="gasPrice">Integer of the gasPrice used for each paid gas (optional)</param>
        /// <param name="gasLimit">Integer of the gas proviced for the transaction execution (optional). Can be zero, but this parameter may be needed by some executions.</param>
        /// <param name="data">Hash of the method signature and encoded parameters</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Esitmated gas in the first element of the tuple if success, otherwise error in the second element of the tuple</returns>
        Task<Result<long>> EstimateGasAsync(
            string to,
            string? from = null,
            BigInteger? value = null,
            BigInteger? gasPrice = null,
            BigInteger? gasLimit = null,
            string? data = null,
            CancellationToken cancellationToken = default);
    }
}