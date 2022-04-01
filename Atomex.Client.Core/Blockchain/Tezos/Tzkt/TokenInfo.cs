using System.Text.Json.Serialization;
using Atomex.Common;

namespace Atomex.Blockchain.Tezos.Tzkt
{
    public class TokenInfo
    {
        /// <summary>
        /// Internal TzKT id (not the same as `tokenId`).
        /// </summary>
        [JsonPropertyName("id")]
        public int Id { get; set; }

        /// <summary>
        /// Contract, created the token.
        /// </summary>
        [JsonPropertyName("contract")]
        public Alias Contract { get; set; }

        /// <summary>
        /// Token id, unique within the contract.
        /// </summary>
        [JsonPropertyName("tokenId")]
        public string TokenId { get; set; }

        /// <summary>
        /// Token standard (either `fa1.2` or `fa2`).
        /// </summary>
        [JsonPropertyName("standard")]
        public string Standard { get; set; }

        /// <summary>
        /// Token metadata.  
        /// **[sortable]**
        /// </summary>
        [JsonPropertyName("metadata")]
        public RawJson Metadata { get; set; }
    }
}