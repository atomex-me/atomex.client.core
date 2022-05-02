using System;
using Atomex.Blockchain.Abstract;

namespace Atomex.Blockchain
{
    public class TransactionEventArgs : EventArgs
    {
        public IBlockchainTransaction_OLD Transaction { get; }

        public TransactionEventArgs(IBlockchainTransaction_OLD transaction)
        {
            Transaction = transaction ?? throw new ArgumentNullException(nameof(transaction));
        }
    }
}