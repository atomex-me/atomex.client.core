using System.Text.Json.Serialization;

namespace Atomex.Blockchain.Tezos.Tzkt.Operations
{
    public class ManagerOperation : Operation
    {
        [JsonPropertyName("counter")]
        public int Counter { get; set; }
        [JsonPropertyName("gasLimit")]
        public int GasLimit { get; set; }
        [JsonPropertyName("bakerFee")]
        public long BakerFee { get; set; }
        [JsonPropertyName("storageLimit")]
        public int StorageLimit { get; set; }
    }
}