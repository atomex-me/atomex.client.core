using System.Text.Json.Serialization;

namespace Atomex.Blockchain.Tezos.Tzkt.Operations
{
    public class DoubleBakingOperation : Operation
    {
        [JsonPropertyName("accusedLevel")]
        public int AccusedLevel { get; set; }
        [JsonPropertyName("accuser")]
        public Alias Accuser { get; set; }
        [JsonPropertyName("accuserRewards")]
        public long AccuserRewards { get; set; }
        [JsonPropertyName("offender")]
        public Alias Offender { get; set; }
        [JsonPropertyName("offenderLostDeposits")]
        public long OffenderLostDeposits { get; set; }
        [JsonPropertyName("offenderLostRewards")]
        public long OffenderLostRewards { get; set; }
        [JsonPropertyName("offenderLostFees")]
        public long OffenderLostFees { get; set; }
    }
}