using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Atomex.Blockchain.Tezos.Tzkt
{
    public class Token
    {
        [JsonPropertyName("contract")]
        public string Contract { get; set; }
        [JsonPropertyName("token_id")]
        public decimal TokenId { get; set; } = 0; // FA1.2 default
        [JsonPropertyName("symbol")]
        public string Symbol { get; set; }
        [JsonPropertyName("name")]
        public string Name { get; set; }
        [JsonPropertyName("decimals")]
        public int Decimals { get; set; } = 0; // NFT default
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
}