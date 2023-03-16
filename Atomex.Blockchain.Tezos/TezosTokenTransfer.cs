using System;
using System.Numerics;

using Atomex.Blockchain.Abstract;
using Atomex.Common;

namespace Atomex.Blockchain.Tezos
{
    public class TezosTokenTransfer : ITransaction, ITokenTransfer
    {
        public string Id { get; set; }
        public string Currency { get; set; }
        public TransactionStatus Status { get; set; }
        public DateTimeOffset? CreationTime { get; set; }
        public DateTimeOffset? BlockTime { get; set; }
        public long BlockHeight { get; set; }
        public long Confirmations { get; set; }
        public bool IsConfirmed => Confirmations > 0;
        public string Contract => Token.Contract;
        public BigInteger TokenId => Token.TokenId;
        public string From { get; set; }
        public string To { get; set; }
        public string Amount { get; set; }
        public Token Token { get; set; }
        public string FromAlias { get; set; }
        public string ToAlias { get; set; }
        public string ContractAlias { get; set; }

        public BigInteger GetAmount() => BigInteger.TryParse(Amount, out var amount)
            ? amount
            : 0;

        public string GetAlias(TransactionType type) => type.HasFlag(TransactionType.Input)
            ? !string.IsNullOrEmpty(FromAlias)
                ? FromAlias
                : From.TruncateAddress()
            : !string.IsNullOrEmpty(ToAlias)
                ? ToAlias
                : To.TruncateAddress();
    }
}