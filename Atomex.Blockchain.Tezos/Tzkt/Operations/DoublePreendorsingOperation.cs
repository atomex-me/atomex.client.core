using System.Text.Json.Serialization;

namespace Atomex.Blockchain.Tezos.Tzkt.Operations
{
    public class DoublePreendorsingOperation : Operation
    {
        [JsonPropertyName("accusedLevel")]
        public int AccusedLevel { get; set; }
        [JsonPropertyName("accuser")]
        public Alias Accuser { get; set; }
        [JsonPropertyName("accuserRewards")]
        public long AccuserRewards { get; set; }
        [JsonPropertyName("offender")]
        public Alias Offender { get; set; }
        [JsonPropertyName("offenderLoss")]
        public long OffenderLoss { get; set; }
    }
}