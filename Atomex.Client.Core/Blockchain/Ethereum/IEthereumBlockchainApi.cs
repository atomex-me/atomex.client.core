using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Atomex.Blockchain.Abstract;
using Atomex.Common;

namespace Atomex.Blockchain.Ethereum
{
    public interface IEthereumBlockchainApi : IBlockchainApi
    {
        Task<Result<BigInteger>> GetTransactionCountAsync(
            string address,
            CancellationToken cancellationToken = default(CancellationToken));

        Task<Result<IEnumerable<IBlockchainTransaction>>> GetTransactionsAsync(
            string address,
            CancellationToken cancellationToken = default(CancellationToken));
    }
}