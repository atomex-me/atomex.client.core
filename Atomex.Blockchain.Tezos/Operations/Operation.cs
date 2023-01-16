using System;
using System.Text.Json.Serialization;

using Atomex.Blockchain.Tezos.Common;

namespace Atomex.Blockchain.Tezos.Operations
{
    [JsonConverter(typeof(OperationJsonConverter))]
    public class Operation
    {
        [JsonPropertyName("id")]
        public long Id { get; set; }
        [JsonPropertyName("type")]
        public string Type { get; set; }
        [JsonPropertyName("level")]
        public int BlockLevel { get; set; }
        [JsonPropertyName("timestamp")]
        public DateTimeOffset BlockTime { get; set; }
        [JsonPropertyName("block")]
        public string Block { get; set; }
        [JsonPropertyName("hash")]
        public string Hash { get; set; }
        [JsonPropertyName("sender")]
        public Alias Sender { get; set; }
        [JsonPropertyName("gasUsed")]
        public int GasUsed { get; set; }
        [JsonPropertyName("status")]
        public string Status { get; set; }
    }
}