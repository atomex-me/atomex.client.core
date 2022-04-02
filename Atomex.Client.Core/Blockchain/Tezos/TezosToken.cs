using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

using Atomex.Blockchain.Abstract;
using Atomex.Common;

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
        public decimal TokenId { get; set; } = 0;  // FA1.2 default
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
    }
    public class TokenBalance : Token
    {
        [JsonPropertyName("balance")]
        public string Balance { get; set; } = "0";

        public decimal GetTokenBalance() =>
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

        [JsonPropertyName("timestamp")]
        public DateTimeOffset TimeStamp { get; set; }
        [JsonPropertyName("level")]
        public int Level { get; set; }
        [JsonPropertyName("from")]
        public string From { get; set; }
        [JsonPropertyName("to")]
        public string To { get; set; }
        [JsonPropertyName("amount")]
        public string Amount { get; set; }
        [JsonPropertyName("token")]
        public Token Token { get; set; }

        public decimal GetTransferAmount() =>
            Amount.TryParseWithRound(Token.Decimals, out var result)
                ? result
                : 0;
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
    }
}