using System.Text.Json.Serialization;

namespace Atomex.Blockchain.Tezos.Operations
{
    public class RevelationPenaltyOperation : Operation
    {
        [JsonPropertyName("baker")]
        public Alias Baker { get; set; }
        [JsonPropertyName("missedLevel")]
        public int MissedLevel { get; set; }
        [JsonPropertyName("lostReward")]
        public long LostReward { get; set; }
        [JsonPropertyName("lostFees")]
        public long LostFees { get; set; }
    }
}