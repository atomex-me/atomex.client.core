using System;
using System.Text.Json.Serialization;

namespace Atomex.Blockchain.Tezos.Tzkt
{
    public class TokenTransfer
    {
        /// <summary>
        /// Internal TzKT id.  
        /// **[sortable]**
        /// </summary>
        [JsonPropertyName("id")]
        public int Id { get; set; }

        /// <summary>
        /// Level of the block, at which the token transfer was made.  
        /// **[sortable]**
        /// </summary>
        [JsonPropertyName("level")]
        public int Level { get; set; }

        /// <summary>
        /// Timestamp of the block, at which the token transfer was made.
        /// </summary>
        [JsonPropertyName("timestamp")]
        public DateTime Timestamp { get; set; }

        /// <summary>
        /// Token info.  
        /// Click on the field to expand more details.
        /// </summary>
        [JsonPropertyName("token")]
        public TokenInfo Token { get; set; }

        /// <summary>
        /// Sender account.  
        /// Click on the field to expand more details.
        /// </summary>
        [JsonPropertyName("from")]
        public Alias From { get; set; }

        /// <summary>
        /// Target account.  
        /// Click on the field to expand more details.
        /// </summary>
        [JsonPropertyName("to")]
        public Alias To { get; set; }

        /// <summary>
        /// Amount of tokens transferred (raw value, not divided by `decimals`).  
        /// **[sortable]**
        /// </summary>
        [JsonPropertyName("amount")]
        public string Amount { get; set; }

        /// <summary>
        /// Internal TzKT id of the transaction operation, caused the token transfer.
        /// </summary>
        [JsonPropertyName("transactionId")]
        public int? TransactionId { get; set; }

        /// <summary>
        /// Internal TzKT id of the origination operation, caused the token transfer.
        /// </summary>
        [JsonPropertyName("originationId")]
        public int? OriginationId { get; set; }

        /// <summary>
        /// Internal TzKT id of the migration operation, caused the token transfer.
        /// </summary>
        [JsonPropertyName("migrationId")]
        public int? MigrationId { get; set; }
    }
}