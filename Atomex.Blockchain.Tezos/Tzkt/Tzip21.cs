using System.Collections.Generic;
using System.Text.Json.Serialization;

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
}