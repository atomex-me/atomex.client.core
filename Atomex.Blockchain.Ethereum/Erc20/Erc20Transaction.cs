using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

using Atomex.Blockchain.Abstract;

namespace Atomex.Blockchain.Ethereum.Erc20
{
    public class Erc20Transfer
    {
        public string From { get; set; }
        public string To { get; set; }
        public BigInteger Value { get; set; }
        public TransactionType Type { get; set; }
    }

    public class Erc20Transaction : ITransaction
    {
        public string Id { get; set; }
        public string Currency { get; set; }
        public TransactionStatus Status { get; set; }
        public TransactionType Type => Transfers
            .Select(t => t.Type)
            .Aggregate(TransactionType.Unknown, (s, t) => s |= t);
        public DateTimeOffset? CreationTime { get; set; }
        public DateTimeOffset? BlockTime { get; set; }
        public long BlockHeight { get; set; }
        public long Confirmations { get; set; }
        public bool IsConfirmed => Confirmations > 0;
        public bool IsTypeResolved => Transfers
            .TrueForAll(t => t.Type != TransactionType.Unknown);

        public BigInteger Nonce { get; set; }
        public BigInteger GasPrice { get; set; }
        public BigInteger GasLimit { get; set; }
        public BigInteger GasUsed { get; set; }

        public List<Erc20Transfer> Transfers { get; set; }
    }
}