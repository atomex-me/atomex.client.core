using System;
using System.Collections.Generic;
using System.Numerics;

using Atomex.Blockchain.Abstract;

namespace Atomex.Blockchain.Ethereum.Erc20
{
    public class Erc20Transfer
    {
        public string From { get; set; }
        public string To { get; set; }
        public BigInteger Value { get; set; }
    }

    public class Erc20Transaction : Transaction
    {
        public override string TxId { get; set; }
        public override string Currency { get; set; }
        public override TransactionStatus Status { get; set; }
        public override DateTimeOffset? CreationTime { get; set; }
        public override DateTimeOffset? BlockTime { get; set; }
        public override long BlockHeight { get; set; }
        public override long Confirmations { get; set; }

        public BigInteger Nonce { get; set; }
        public BigInteger GasPrice { get; set; }
        public BigInteger GasLimit { get; set; }
        public BigInteger GasUsed { get; set; }

        public List<Erc20Transfer> Transfers { get; set; }
    }
}