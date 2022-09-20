using Newtonsoft.Json;

using Atomex.Common;

namespace Atomex.Blockchain.Tezos.Tzkt
{
    public class TokenInfo
    {
        /// <summary>
        /// Internal TzKT id (not the same as `tokenId`).
        /// </summary>
        [JsonProperty("id")]
        public long Id { get; set; }

        /// <summary>
        /// Contract, created the token.
        /// </summary>
        [JsonProperty("contract")]
        public AddressAccount Contract { get; set; }

        /// <summary>
        /// Token id, unique within the contract.
        /// </summary>
        [JsonProperty("tokenId")]
        public string TokenId { get; set; }

        /// <summary>
        /// Token standard (either `fa1.2` or `fa2`).
        /// </summary>
        [JsonProperty("standard")]
        public string Standard { get; set; }

        /// <summary>
        /// Token metadata.  
        /// **[sortable]**
        /// </summary>
        [JsonProperty("metadata")]
        [JsonConverter(typeof(ObjectAsRawStringJsonConverter))]
        public string Metadata { get; set; }

        public Token ToToken()
        {
            var token = new Token()
            {
                Contract      = Contract.Address,
                ContractAlias = Contract.Alias,
                Standard      = Standard,
                TokenId       = decimal.Parse(TokenId),
            };

            if (Metadata != null)
            {
                try
                {
                    var metadata = JsonConvert.DeserializeObject<Tzip21>(Metadata);
                    token.Name         = metadata.Name;
                    token.Symbol       = metadata.Symbol;
                    token.Decimals     = metadata.Decimals;
                    token.Description  = metadata.Description;
                    token.ArtifactUri  = metadata.ArtifactUri;
                    token.DisplayUri   = metadata.DisplayUri;
                    token.ThumbnailUri = metadata.ThumbnailUri;
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