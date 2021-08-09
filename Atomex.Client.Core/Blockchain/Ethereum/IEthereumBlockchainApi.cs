using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;

using Nethereum.Contracts;

using Atomex.Blockchain.Abstract;
using Atomex.Common;

namespace Atomex.Blockchain.Ethereum
{
    public interface IEthereumBlockchainApi
    {
        Task<Result<decimal>> GetERC20AllowanceAsync(
            EthereumTokens.Erc20Config erc20,
            string tokenAddress,
            FunctionMessage allowanceMessage,
            CancellationToken cancellationToken = default);

        Task<Result<BigInteger>> GetTransactionCountAsync(
            string address,
            bool pending = true,
            CancellationToken cancellationToken = default);

        Task<Result<BigInteger>> TryGetTransactionCountAsync(
            string address,
            bool pending = true,
            int attempts = 3,
            int attemptsIntervalMs = 1000,
            CancellationToken cancellationToken = default);

        Task<Result<IEnumerable<IBlockchainTransaction>>> GetTransactionsAsync(
            string address,
            long fromBlock = 0,
            long toBlock = long.MaxValue,
            CancellationToken cancellationToken = default);

        Task<Result<IEnumerable<IBlockchainTransaction>>> TryGetTransactionsAsync(
            string address,
            long fromBlock = 0,
            long toBlock = long.MaxValue,
            int attempts = 3,
            int attemptsIntervalMs = 1000,
            CancellationToken cancellationToken = default);

        Task<Result<long>> GetBlockByTimeStampAsync(
            long unixTimeStamp,
            CancellationToken cancellationToken = default);

        Task<Result<long>> TryGetBlockByTimeStampAsync(
            long unixTimeStamp,
            int attempts = 3,
            int attemptsIntervalMs = 1000,
            CancellationToken cancellationToken = default);
    }
}