using System;

using Atomex.Blockchain.Abstract;
using Atomex.Common;
using Atomex.ViewModels;

namespace Atomex.Blockchain.Tezos
{
    public class TokenTransfer : IBlockchainTransaction
    {
        public string Id { get; set; }
        public string Currency { get; set; }
        public BlockInfo BlockInfo => new()
        {
            BlockHash     = null,
            BlockHeight   = Level,
            BlockTime     = TimeStamp.UtcDateTime,
            Confirmations = 1,
            FirstSeen     = TimeStamp.UtcDateTime
        };
        public BlockchainTransactionState State { get; set; } = BlockchainTransactionState.Confirmed;
        public BlockchainTransactionType Type { get; set; }
        public DateTime? CreationTime => TimeStamp.UtcDateTime;
        public bool IsConfirmed => true;
        public string Contract => Token.Contract;
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

        public string GetAlias() => Type.HasFlag(BlockchainTransactionType.Input)
            ? !string.IsNullOrEmpty(FromAlias)
                ? FromAlias
                : From.TruncateAddress()
            : !string.IsNullOrEmpty(ToAlias)
                ? ToAlias
                : To.TruncateAddress();
    }
}