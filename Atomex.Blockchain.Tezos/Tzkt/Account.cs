using System.Text.Json.Serialization;

namespace Atomex.Blockchain.Tezos.Tzkt
{
    public class Account
    {
        [JsonPropertyName("type")]
        public string Type { get; set; }
        [JsonPropertyName("address")]
        public string Address { get; set; }
        [JsonPropertyName("alias")]
        public string Alias { get; set; }
        [JsonPropertyName("revealed")]
        public bool Revealed { get; set; }
        [JsonPropertyName("balance")]
        public long Balance { get; set; }
        [JsonPropertyName("counter")]
        public int Counter { get; set; }
        [JsonPropertyName("delegationLevel")]
        public int DelegationLevel { get; set; }
        [JsonPropertyName("delegationTime")]
        public string DelegationTime { get; set; }
        [JsonPropertyName("activeTokensCount")]
        public int ActiveTokensCount { get; set; }
        [JsonPropertyName("tokenBalancesCount")]
        public int TokenBalancesCount { get; set; }
        [JsonPropertyName("tokenTransfersCount")]
        public int TokenTransfersCount { get; set; }
        [JsonPropertyName("numTransactions")]
        public int NumberOfTransactions { get; set; }
        [JsonPropertyName("firstActivity")]
        public int FirstActivity { get; set; }
        [JsonPropertyName("firstActivityTime")]
        public string FirstActivityTime { get; set; }
        [JsonPropertyName("lastActivity")]
        public int LastActivity { get; set; }
        [JsonPropertyName("lastActivityTime")]
        public string LastActivityTime { get; set; }
    }
}