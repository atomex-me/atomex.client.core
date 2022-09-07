using System.Collections.Generic;

using Atomex.Blockchain.Abstract;

namespace Atomex.Wallet
{
    public class TransactionsChangedEventArgs
    {
        public IEnumerable<IBlockchainTransaction> Transactions { get; }

        public TransactionsChangedEventArgs(IBlockchainTransaction transaction)
        {
            Transactions = new List<IBlockchainTransaction>() { transaction };
        }

        public TransactionsChangedEventArgs(IEnumerable<IBlockchainTransaction> transactions)
        {
            Transactions = transactions;
        }
    }
}