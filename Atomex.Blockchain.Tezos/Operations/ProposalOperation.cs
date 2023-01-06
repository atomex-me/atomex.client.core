using System.Text.Json.Serialization;

namespace Atomex.Blockchain.Tezos.Operations
{
    public class ProposalOperation : Operation
    {
        [JsonPropertyName("period")]
        public Period Period { get; set; }
        [JsonPropertyName("proposal")]
        public ProposalAlias Proposal { get; set; }
        [JsonPropertyName("delegate")]
        public Alias Delegate { get; set; }
        [JsonPropertyName("rolls")]
        public int Rolls { get; set; }
        [JsonPropertyName("duplicated")]
        public bool Duplicated { get; set; }
    }
}