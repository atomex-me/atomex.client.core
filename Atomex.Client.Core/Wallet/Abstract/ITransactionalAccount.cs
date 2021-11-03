using System.Threading;
using System.Threading.Tasks;

using Atomex.Blockchain.Abstract;

namespace Atomex.Wallet.Abstract
{
    public interface ITransactionalAccount
    {
        Task UpsertTransactionAsync(
            IBlockchainTransaction tx,
            bool updateBalance = false,
            bool notifyIfUnconfirmed = true,
            bool notifyIfBalanceUpdated = true,
            CancellationToken cancellationToken = default);
    }
}