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

    public class Erc20Transaction : ITransaction, ITokenTransfer
    {
        public string Id { get; set; }
        public string Currency { get; set; }
        public string Contract { get; set; }
        public BigInteger TokenId => BigInteger.Zero;
        public TransactionStatus Status { get; set; }
        public DateTimeOffset? CreationTime { get; set; }
        public DateTimeOffset? BlockTime { get; set; }
        public long BlockHeight { get; set; }
        public long Confirmations { get; set; }
        public bool IsConfirmed => Confirmations > 0;
        public BigInteger Nonce { get; set; }
        public BigInteger GasPrice { get; set; }
        public BigInteger GasLimit { get; set; }
        public BigInteger GasUsed { get; set; }
        public List<Erc20Transfer> Transfers { get; set; }
    }
}