using System.Text.Json.Serialization;

namespace Atomex.Blockchain.Tezos.Tzkt.Operations
{
    public class DelegationOperation : ManagerOperation
    {
        [JsonPropertyName("amount")]
        public long Amount { get; set; }
        [JsonPropertyName("prevDelegate")]
        public Alias PrevDelegate { get; set; }
        [JsonPropertyName("newDelegate")]
        public Alias NewDelegate { get; set; }
    }
}