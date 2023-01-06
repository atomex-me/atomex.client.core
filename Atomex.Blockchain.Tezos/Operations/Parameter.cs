using System.Text.Json.Serialization;

namespace Atomex.Blockchain.Tezos.Operations
{
    public class Parameter
    {
        [JsonPropertyName("entrypoint")]
        public string Entrypoint { get; set; }
        [JsonPropertyName("value")]
        public object Value { get; set; }
    }
}