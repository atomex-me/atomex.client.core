using System.Text.Json.Serialization;

namespace Atomex.Blockchain.Tezos.Tzkt.Operations
{
    public class VdfRevelationOperation : Operation
    {
        [JsonPropertyName("baker")]
        public Alias Baker { get; set; }
        [JsonPropertyName("cycle")]
        public int Cycle { get; set; }
        [JsonPropertyName("solution")]
        public string Solution { get; set; }
        [JsonPropertyName("proof")]
        public string Proof { get; set; }
        [JsonPropertyName("reward")]
        public long Reward { get; set; }
    }
}