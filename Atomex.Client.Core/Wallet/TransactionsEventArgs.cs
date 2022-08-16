using System.Collections.Generic;

using Atomex.Blockchain.Abstract;

namespace Atomex.Wallet
{
    public class TransactionsEventArgs
    {
        public IEnumerable<IBlockchainTransaction> Transactions { get; }

        public TransactionsEventArgs(IBlockchainTransaction transaction)
        {
            Transactions = new List<IBlockchainTransaction>() { transaction };
        }

        public TransactionsEventArgs(IEnumerable<IBlockchainTransaction> transactions)
        {
            Transactions = transactions;
        }
    }
}