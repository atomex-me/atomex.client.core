using System.Text.Json.Serialization;

namespace Atomex.Blockchain.Tezos.Operations
{
    public class EndorsmentOperation : Operation
    {
        [JsonPropertyName("delegate")]
        public Alias Delegate { get; set; }
        [JsonPropertyName("slots")]
        public int Slots { get; set; }
        [JsonPropertyName("deposit")]
        public long Deposit { get; set; }
        [JsonPropertyName("rewards")]
        public long Rewards { get; set; }
    }
}