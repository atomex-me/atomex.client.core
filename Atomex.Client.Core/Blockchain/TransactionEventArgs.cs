using System;
using Atomex.Blockchain.Abstract;

namespace Atomex.Blockchain
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