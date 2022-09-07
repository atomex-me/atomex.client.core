using System.Collections.Generic;
using System.Linq;

using Atomex.Blockchain.Abstract;

namespace Atomex.Wallet
{
    public class TransactionsChangedEventArgs
    {
        public IEnumerable<IBlockchainTransaction> Transactions { get; }
        public string Currency => Transactions?.FirstOrDefault()?.Currency;

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