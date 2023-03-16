using System.Text.Json.Serialization;

namespace Atomex.Blockchain.Tezos.Tzkt
{
    public class TokenContract
    {
        public string Id => Address;

        [JsonPropertyName("address")]
        public string Address { get; set; }
        [JsonPropertyName("name")]
        public string Name { get; set; }
        [JsonPropertyName("type")]
        public string Type { get; set; }

        public string GetContractType() => Type;
    }
}