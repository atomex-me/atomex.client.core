using System.Text.Json.Serialization;

namespace Atomex.Blockchain.Tezos.Operations
{
    public class MigrationOperation : Operation
    {
        [JsonPropertyName("account")]
        public Alias Account { get; set; }
        [JsonPropertyName("balanceChange")]
        public long BalanceChange { get; set; }
    }
}