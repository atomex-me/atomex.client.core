using System;
using System.Text.Json.Serialization;

namespace Atomex.Blockchain.Tezos.Common
{
    public class TezosAccount
    {
        [JsonPropertyName("alias")]
        public string Alias { get; set; }
        [JsonPropertyName("address")]
        public string Address { get; set; }
        [JsonPropertyName("revealed")]
        public bool IsRevealed { get; set; }
        [JsonPropertyName("balance")]
        public long Balance { get; set; }
        [JsonPropertyName("counter")]
        public int Counter { get; set; }
        [JsonPropertyName("lastActivityTime")]
        public DateTimeOffset LastActivityTime { get; set; }
        [JsonPropertyName("numActivations")]
        public int NumOfActivations { get; set; }
        [JsonPropertyName("numDelegations")]
        public int NumOfDelegations { get; set; }
        [JsonPropertyName("numOriginations")]
        public int NumOfOriginations { get; set; }
        [JsonPropertyName("numTransactions")]
        public int NumOfTransactions { get; set; }
        [JsonPropertyName("numReveals")]
        public int NumOfReveals { get; set; }
        public bool HasActivity => NumOfActivations +
            NumOfDelegations +
            NumOfOriginations +
            NumOfTransactions +
            NumOfReveals > 0;
    }
}