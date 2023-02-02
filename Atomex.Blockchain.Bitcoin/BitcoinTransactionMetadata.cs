using System.Numerics;

using Atomex.Blockchain.Abstract;

namespace Atomex.Blockchain.Bitcoin
{
    public class BitcoinTransactionMetadata : ITransactionMetadata
    {
        public string Id { get; set; }
        public TransactionType Type { get; set; }
        public BigInteger Amount { get; set; }
    }
}