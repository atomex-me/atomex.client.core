using System.Text.Json.Serialization;

namespace Atomex.Blockchain.Tezos.Tzkt.Operations
{
    public class OriginationOperation : ManagerOperation
    {
        [JsonPropertyName("initiator")]
        public Alias Initiator { get; set; }
        [JsonPropertyName("nonce")]
        public int Nonce { get; set; }
        [JsonPropertyName("contractBalance")]
        public long ContractBalance { get; set; }
        [JsonPropertyName("contractManager")]
        public Alias ContractManager { get; set; }
        [JsonPropertyName("contractDelegate")]
        public Alias ContractDelegate { get; set; }
    }
}