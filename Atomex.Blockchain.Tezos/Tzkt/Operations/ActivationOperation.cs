using System.Text.Json.Serialization;

namespace Atomex.Blockchain.Tezos.Tzkt.Operations
{
    public class ActivationOperation : Operation
    {
        [JsonPropertyName("account")]
        public Alias Account { get; set; }
        [JsonPropertyName("balance")]
        public long Balance { get; set; }
    }
}