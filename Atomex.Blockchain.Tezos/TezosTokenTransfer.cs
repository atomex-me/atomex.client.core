using System;

using Atomex.Blockchain.Abstract;
using Atomex.Blockchain.Tezos.Tzkt;
using Atomex.Common;

namespace Atomex.Blockchain.Tezos
{
    public class TezosTokenTransfer : Transaction
    {
        public DateTimeOffset TimeStamp { get; set; }
        public int Level { get; set; }
        public string From { get; set; }
        public string To { get; set; }
        public string Amount { get; set; }
        public Token Token { get; set; }
        public string FromAlias { get; set; }
        public string ToAlias { get; set; }
        public string ContractAlias { get; set; }

        public decimal GetTransferAmount() =>
            Amount.TryParseWithRound(Token.Decimals, out var result)
                ? result
                : 0;
    }
}