using System.Numerics;
using System.Threading;
using System.Threading.Tasks;

using Atomex.Blockchain.Abstract;
using Atomex.Common;

namespace Atomex.Blockchain
{
    public abstract class BlockchainApi : IBlockchainApi
    {
        public abstract Task<Result<BigInteger>> GetBalanceAsync(
            string address,
            CancellationToken cancellationToken = default);

        public abstract Task<Result<ITransaction>> GetTransactionAsync(
            string txId,
            CancellationToken cancellationToken = default);

        public abstract Task<Result<string>> BroadcastAsync(
            ITransaction transaction,
            CancellationToken cancellationToken = default);
    }
}