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
        [JsonPropertyName("creators")]
        public List<string> Creators { get; set; }
    }

    public class Token
    {
        [JsonPropertyName("contract")]
        public string Contract { get; set; }

        [JsonPropertyName("token_id")]
        public int TokenId { get; set; } = 0;  // FA1.2 default
        [JsonPropertyName("symbol")]
        public string Symbol { get; set; }
        [JsonPropertyName("name")]
        public string Name { get; set; }
        [JsonPropertyName("decimals")]
        public int Decimals { get; set; } = 0;  // NFT default
        [JsonPropertyName("description")]
        public string Description { get; set; }
        [JsonPropertyName("artifact_uri")]
        public string ArtifactUri { get; set; }
        [JsonPropertyName("display_uri")]
        public string DisplayUri { get; set; }
        [JsonPropertyName("thumbnail_uri")]
        public string ThumbnailUri { get; set; }
        [JsonPropertyName("creators")]
        public List<string> Creators { get; set; }

        public bool HasDescription =>
            !string.IsNullOrEmpty(Description);

        public bool IsNft =>
            !string.IsNullOrEmpty(ArtifactUri);
    }
    public class TokenBalance : Token
    {
        [JsonPropertyName("balance")]
        public string Balance { get; set; } = "0";
        public decimal? ParsedBalance { get; set; }

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