using System;
using Atomex.Blockchain.Abstract;

namespace Atomex.Blockchain
{
    public class TransactionEventArgs : EventArgs
    {
        public ITransaction Transaction { get; }

        public TransactionEventArgs(ITransaction transaction)
        {
            Transaction = transaction ?? throw new ArgumentNullException(nameof(transaction));
        }
    }
}