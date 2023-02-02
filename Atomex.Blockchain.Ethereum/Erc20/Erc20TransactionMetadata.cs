using System.Collections.Generic;
using System.Linq;
using System.Numerics;

using Atomex.Blockchain.Abstract;

namespace Atomex.Blockchain.Ethereum.Erc20
{
    public class Erc20TransactionMetadata : ITransactionMetadata
    {
        public string Id { get; set; }
        public TransactionType Type => TransfersTypes?.Aggregate(TransactionType.Unknown, (s, t) => s |= t) ?? TransactionType.Unknown;
        public BigInteger Amount { get; set; }
        public List<TransactionType> TransfersTypes { get; set; }
    }
}