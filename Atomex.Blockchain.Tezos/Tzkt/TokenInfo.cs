using System.Text.Json;
using System.Text.Json.Serialization;

using Atomex.Blockchain.Tezos.Common;

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
        public AddressAccount Contract { get; set; }

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
        [JsonConverter(typeof(ObjectAsRawStringJsonConverter))]
        public string Metadata { get; set; }

        public Token ToToken()
        {
            var token = new Token()
            {
                Contract = Contract.Address,
                TokenId = decimal.Parse(TokenId),
            };

            if (Metadata != null) 
            {
                try
                {
                    var metadata       = JsonSerializer.Deserialize<Tzip21>(Metadata);
                    token.Name         = metadata.Name;
                    token.Symbol       = metadata.Symbol;
                    token.Decimals     = metadata.Decimals;
                    token.Description  = metadata.Description;
                    token.ArtifactUri  = metadata.ArtifactUri;
                    token.DisplayUri   = metadata.DisplayUri;
                    token.ThumbnailUri = metadata.ThumbnailUri;
                    token.Creators     = metadata.Creators;
                }
                catch
                {
                    // Invalid metadata JSON            
                }
            }

            return token;
        }
    }
}