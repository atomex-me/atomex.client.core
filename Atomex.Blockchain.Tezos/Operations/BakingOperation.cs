using System.Text.Json.Serialization;

namespace Atomex.Blockchain.Tezos.Operations
{
    public class BakingOperation : Operation
    {
        [JsonPropertyName("baker")]
        public Alias Baker { get; set; }
        [JsonPropertyName("priority")]
        public int Priority { get; set; }
        [JsonPropertyName("deposit")]
        public long Deposit { get; set; }
        [JsonPropertyName("reward")]
        public long Reward { get; set; }
        [JsonPropertyName("fees")]
        public long Fees { get; set; }
    }
}