using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

using Atomex.Blockchain.Abstract;
using Atomex.Common;
using Atomex.ViewModels;

namespace Atomex.Blockchain.Tezos
{
    public class Tzip21
    {
        [JsonPropertyName("symbol")]
        public string Symbol { get; set; }
        [JsonPropertyName("name")]
        public string Name { get; set; }
        [JsonPropertyName("decimals")]
        public int Decimals { get; set; }
        [JsonPropertyName("description")]
        public string Description { get; set; }
        [JsonPropertyName("artifactUri")]
        public string ArtifactUri { get; set; }
        [JsonPropertyName("displayUri")]
        public string DisplayUri { get; set; }
        [JsonPropertyName("thumbnailUri")]
        public string ThumbnailUri { get; set; }
    }

    public class Token
    {
        public string Contract { get; set; }
        public string ContractAlias { get; set; }
        public string Standard { get; set; }
        public decimal TokenId { get; set; } = 0;  // FA1.2 default
        public string Symbol { get; set; }
        public string Name { get; set; }
        public int Decimals { get; set; } = 0;  // NFT default
        public string Description { get; set; }
        public string ArtifactUri { get; set; }
        public string DisplayUri { get; set; }
        public string ThumbnailUri { get; set; }

        public bool HasDescription =>
            !string.IsNullOrEmpty(Description);

        public bool IsNft =>
            !string.IsNullOrEmpty(ArtifactUri);

        public string ContractType => Standard switch
        {
            "fa1.2" => "FA12",
            "fa2" => "FA2",
            _ => Standard
        };
    }
    public class TokenBalance : Token
    {
        public string Address { get; set; }
        public string Balance { get; set; } = "0";
        public decimal? ParsedBalance { get; set; }
        public int TransfersCount { get; set; }

        public decimal GetTokenBalance() => ParsedBalance ??=
            Balance.TryParseWithRound(Decimals, out var result)
                ? result
                : 0;
    }

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

    public class TokenContract
    {
        public string Id => Address;

        [JsonPropertyName("address")]
        public string Address { get; set; }
        [JsonPropertyName("name")]
        public string Name { get; set; }
        [JsonPropertyName("type")]
        public string Type { get; set; }

        public string GetContractType() => Type;
    }
}