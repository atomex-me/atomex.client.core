using System.Collections.Generic;
using System.Linq;

using Atomex.Blockchain.Abstract;

namespace Atomex.Wallet
{
    public class TransactionsChangedEventArgs
    {
        public IEnumerable<ITransaction> Transactions { get; }
        public string Currency => Transactions?.FirstOrDefault()?.Currency;

        public TransactionsChangedEventArgs(ITransaction transaction)
        {
            Transactions = new List<ITransaction>() { transaction };
        }

        public TransactionsChangedEventArgs(IEnumerable<ITransaction> transactions)
        {
            Transactions = transactions;
        }
    }
}