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
    public interface IEthereumBlockchainApi
    {
        Task<Result<BigInteger>> GetErc20BalanceAsync(
            string address,
            string contractAddress,
            CancellationToken cancellationToken = default);

        Task<Result<decimal>> GetErc20AllowanceAsync(
            Erc20Config erc20,
            string tokenAddress,
            FunctionMessage allowanceMessage,
            CancellationToken cancellationToken = default);

        Task<Result<BigInteger>> GetTransactionCountAsync(
            string address,
            bool pending = true,
            CancellationToken cancellationToken = default);

        Task<Result<IEnumerable<ITransaction>>> GetTransactionsAsync(
            string address,
            long fromBlock = 0,
            long toBlock = long.MaxValue,
            CancellationToken cancellationToken = default);

        Task<Result<long>> GetBlockByTimeStampAsync(
            long unixTimeStamp,
            CancellationToken cancellationToken = default);
    }
}