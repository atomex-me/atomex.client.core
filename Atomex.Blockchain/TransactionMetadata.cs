using System.Collections.Generic;
using System.Numerics;

using Atomex.Blockchain.Abstract;

namespace Atomex.Blockchain
{
    public class InternalTransactionMetadata
    {
        public TransactionType Type { get; set; }
        public BigInteger Amount { get; set; }
        public BigInteger Fee { get; set; }
    }

    public class TransactionMetadata : ITransactionMetadata
    {
        public string UniqueId => $"{Id}:{Currency}";
        public string Id { get; set; }
        public string Currency { get; set; }
        public TransactionType Type { get; set; }
        public BigInteger Amount { get; set; }
        public BigInteger Fee { get; set; }
        public List<InternalTransactionMetadata> Internals { get; set; }
    }
}