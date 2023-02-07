using System.Collections.Generic;
using System.Linq;
using System.Numerics;

using Atomex.Blockchain.Abstract;

namespace Atomex.Blockchain.Tezos
{
    public class TezosOperationMetadata : ITransactionMetadata
    {
        public string Id { get; set; }
        public string Currency { get; set; }
        public TransactionType Type => OperationsTypes?.Aggregate(TransactionType.Unknown, (s, t) => s |= t) ?? TransactionType.Unknown;
        public BigInteger Amount { get; set; }
        public List<TransactionType> OperationsTypes { get; set; }
    }
}