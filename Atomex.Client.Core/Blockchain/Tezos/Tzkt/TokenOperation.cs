using System.Text.Json.Serialization;

namespace Atomex.Blockchain.Tezos.Tzkt
{
    public class TokenOperation
    {
        [JsonPropertyName("hash")]
        public string Hash { get; set; }
        [JsonPropertyName("counter")]
        public int Counter { get; set; }
        [JsonPropertyName("id")]
        public long? Id { get; set; }
        [JsonPropertyName("nonce")]
        public int? Nonce { get; set; }
    }
}