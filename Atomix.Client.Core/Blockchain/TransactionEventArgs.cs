using System;
using Atomix.Blockchain.Abstract;

namespace Atomix.Blockchain
{
    public class TransactionEventArgs : EventArgs
    {
        public IBlockchainTransaction Transaction { get; }

        public TransactionEventArgs(IBlockchainTransaction transaction)
        {
            Transaction = transaction ?? throw new ArgumentNullException(nameof(transaction));
        }
    }
}