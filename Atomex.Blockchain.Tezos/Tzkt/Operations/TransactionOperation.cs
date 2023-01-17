using System.Text.Json.Serialization;

namespace Atomex.Blockchain.Tezos.Tzkt.Operations
{
    public class TransactionOperation : ManagerOperation
    {
        [JsonPropertyName("initiator")]
        public Alias Initiator { get; set; }
        [JsonPropertyName("storageUsed")]
        public long StorageUsed { get; set; }
        [JsonPropertyName("storageFee")]
        public long StorageFee { get; set; }
        [JsonPropertyName("allocationFee")]
        public long AllocationFee { get; set; }
        [JsonPropertyName("target")]
        public Alias Target { get; set; }
        [JsonPropertyName("amount")]
        public long Amount { get; set; }
        [JsonPropertyName("parameter")]
        public Parameter Parameter { get; set; }
        [JsonPropertyName("hasInternals")]
        public bool HasInternals { get; set; }
    }
}