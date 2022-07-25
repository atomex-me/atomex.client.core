using System.Text.Json.Serialization;

namespace Atomex.Blockchain.Tezos.Tzkt
{
    public class DelegationAddress : AccountAddress
    {
        [JsonPropertyName("active")]
        public bool Active { get; set; }
    }
}