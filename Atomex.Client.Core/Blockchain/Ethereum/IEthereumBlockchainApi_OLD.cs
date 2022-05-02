using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;

using Nethereum.Contracts;

using Atomex.Blockchain.Abstract;
using Atomex.Common;
using Atomex.EthereumTokens;

namespace Atomex.Blockchain.Ethereum
{
    public interface IEthereumBlockchainApi_OLD
    {
        Task<Result<BigInteger>> GetErc20BalanceAsync(
            string address,
            string contractAddress,
            CancellationToken cancellationToken = default);

        Task<Result<BigInteger>> TryGetErc20BalanceAsync(
            string address,
            string contractAddress,
            int attempts = 3,
            int attemptsIntervalMs = 1000,
            CancellationToken cancellationToken = default);

        Task<Result<decimal>> GetERC20AllowanceAsync(
            Erc20Config erc20,
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

        Task<Result<IEnumerable<IBlockchainTransaction_OLD>>> GetTransactionsAsync(
            string address,
            long fromBlock = 0,
            long toBlock = long.MaxValue,
            CancellationToken cancellationToken = default);

        Task<Result<IEnumerable<IBlockchainTransaction_OLD>>> TryGetTransactionsAsync(
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