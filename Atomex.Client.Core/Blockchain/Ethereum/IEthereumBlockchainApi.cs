using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Atomex.Blockchain.Abstract;
using Atomex.Common;
using Nethereum.Contracts;

namespace Atomex.Blockchain.Ethereum
{
    public interface IEthereumBlockchainApi
    {
        Task<Result<decimal>> GetERC20AllowanceAsync(
            EthereumTokens.ERC20 erc20,
            string tokenAddress,
            FunctionMessage allowanceMessage,
            CancellationToken cancellationToken = default);

        Task<Result<BigInteger>> GetTransactionCountAsync(
            string address,
            CancellationToken cancellationToken = default);

        Task<Result<BigInteger>> TryGetTransactionCountAsync(
            string address,
            int attempts = 3,
            int attemptsIntervalMs = 1000,
            CancellationToken cancellationToken = default);

        Task<Result<IEnumerable<IBlockchainTransaction>>> GetTransactionsAsync(
            string address,
            CancellationToken cancellationToken = default);

        Task<Result<IEnumerable<IBlockchainTransaction>>> TryGetTransactionsAsync(
            string address,
            int attempts = 3,
            int attemptsIntervalMs = 1000,
            CancellationToken cancellationToken = default);
    }
}