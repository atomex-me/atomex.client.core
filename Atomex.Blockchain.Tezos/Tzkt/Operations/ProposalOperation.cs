using System.Text.Json.Serialization;

namespace Atomex.Blockchain.Tezos.Tzkt.Operations
{
    public class ProposalOperation : Operation
    {
        [JsonPropertyName("period")]
        public Period Period { get; set; }
        [JsonPropertyName("proposal")]
        public ProposalAlias Proposal { get; set; }
        [JsonPropertyName("delegate")]
        public Alias Delegate { get; set; }
        [JsonPropertyName("votingPower")]
        public int VotingPower { get; set; }
        [JsonPropertyName("rolls")]
        public int Rolls { get; set; }
        [JsonPropertyName("duplicated")]
        public bool Duplicated { get; set; }
    }
}