using System.Text.Json.Serialization;

namespace Atomex.Blockchain.Tezos.Operations
{
    public class Period
    {
        [JsonPropertyName("index")]
        public int Index { get; set; }
        [JsonPropertyName("epoch")]
        public int Epoch { get; set; }
        [JsonPropertyName("kind")]
        public string Kind { get; set; }
        [JsonPropertyName("firstLevel")]
        public int FirstLevel { get; set; }
        [JsonPropertyName("lastLevel")]
        public int LastLevel { get; set; }
    }
}