using System;
using System.Threading;
using System.Threading.Tasks;

using Atomex.Blockchain;
using Atomex.Blockchain.Abstract;

namespace Atomex.Wallet.Abstract
{
    public interface ITransactionalAccount
    {
        event EventHandler<TransactionEventArgs> UnconfirmedTransactionAdded;

        Task UpsertTransactionAsync(
            IBlockchainTransaction tx,
            bool updateBalance = false,
            bool notifyIfUnconfirmed = true,
            bool notifyIfBalanceUpdated = true,
            CancellationToken cancellationToken = default);
    }
}