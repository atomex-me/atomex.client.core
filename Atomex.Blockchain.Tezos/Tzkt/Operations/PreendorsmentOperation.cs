using System.Text.Json.Serialization;

namespace Atomex.Blockchain.Tezos.Tzkt.Operations
{
    public class PreendorsmentOperation : Operation
    {
        [JsonPropertyName("delegate")]
        public Alias Delegate { get; set; }
        [JsonPropertyName("slots")]
        public int Slots { get; set; }
    }
}