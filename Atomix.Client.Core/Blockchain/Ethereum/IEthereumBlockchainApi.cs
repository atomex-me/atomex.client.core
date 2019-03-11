using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Atomix.Blockchain.Abstract;

namespace Atomix.Blockchain.Ethereum
{
    public interface IEthereumBlockchainApi : IBlockchainApi
    {
        Task<BigInteger> GetTransactionCountAsync(
            string address,
            CancellationToken cancellationToken = default(CancellationToken));

        Task<IEnumerable<IBlockchainTransaction>> GetTransactionsAsync(
            string address,
            CancellationToken cancellationToken = default(CancellationToken));
    }
}