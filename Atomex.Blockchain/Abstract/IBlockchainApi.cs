using System.Numerics;
using System.Threading;
using System.Threading.Tasks;

using Atomex.Common;

namespace Atomex.Blockchain.Abstract
{
    public interface IBlockchainApi
    {
        Task<(BigInteger balance, Error error)> GetBalanceAsync(
            string address,
            CancellationToken cancellationToken = default);

        Task<(Transaction tx, Error error)> GetTransactionAsync(
            string txId,
            CancellationToken cancellationToken = default);

        Task<(string txId, Error error)> BroadcastAsync(
            Transaction transaction,
            CancellationToken cancellationToken = default);
    }
}