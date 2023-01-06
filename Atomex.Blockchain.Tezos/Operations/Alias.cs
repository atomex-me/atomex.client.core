using System.Text.Json.Serialization;

namespace Atomex.Blockchain.Tezos.Operations
{
    public class Alias
    {
        [JsonPropertyName("alias")]
        public string Name { get; set; }
        [JsonPropertyName("address")]
        public string Address { get; set; }
    }
}