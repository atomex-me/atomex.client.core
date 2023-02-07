using System.Numerics;

using Atomex.Blockchain.Abstract;

namespace Atomex.Blockchain
{
    public class TransactionMetadata : ITransactionMetadata
    {
        public string Id { get; set; }
        public string Currency { get; set; }
        public TransactionType Type { get; set; }
        public BigInteger Amount { get; set; }
    }
}