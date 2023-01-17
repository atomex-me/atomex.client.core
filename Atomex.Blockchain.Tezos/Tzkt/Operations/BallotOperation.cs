using System.Text.Json.Serialization;

namespace Atomex.Blockchain.Tezos.Tzkt.Operations
{
    public class BallotOperation : Operation
    {
        [JsonPropertyName("period")]
        public Period Period { get; set; }
        [JsonPropertyName("proposal")]
        public ProposalAlias Proposal { get; set; }
        [JsonPropertyName("delegate")]
        public Alias Delegate { get; set; }
        [JsonPropertyName("rolls")]
        public int Rolls { get; set; }
        [JsonPropertyName("vote")]
        public string Vote { get; set; }
    }
}