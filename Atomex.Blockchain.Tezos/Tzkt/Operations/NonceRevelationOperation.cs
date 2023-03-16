using System.Text.Json.Serialization;

namespace Atomex.Blockchain.Tezos.Tzkt.Operations
{
    public class NonceRevelationOperation : Operation
    {
        [JsonPropertyName("baker")]
        public Alias Baker { get; set; }

        [JsonPropertyName("nonce")]
        public string Nonce { get; set; }
        [JsonPropertyName("reward")]
        public long Reward { get; set; }
        [JsonPropertyName("bakerRewards")]
        public long BakerRewards { get; set; }
        [JsonPropertyName("revealedLevel")]
        public int RevealedLevel { get; set; }
        [JsonPropertyName("revealedCycle")]
        public int RevealedCycle { get; set; }
    }
}