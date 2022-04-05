using System;
using System.Text.Json.Serialization;

namespace Atomex.Blockchain.Tezos.Tzkt
{
    public class TokenBalanceResponse
    {
        /// <summary>
        /// Internal TzKT id.  
        /// **[sortable]**
        /// </summary>
        [JsonPropertyName("id")]
        public int Id { get; set; }

        /// <summary>
        /// Owner account.  
        /// Click on the field to expand more details.
        /// </summary>
        [JsonPropertyName("account")]
        public Alias Account { get; set; }

        /// <summary>
        /// Token info.  
        /// Click on the field to expand more details.
        /// </summary>
        [JsonPropertyName("token")]
        public TokenInfo Token { get; set; }

        /// <summary>
        /// Balance (raw value, not divided by `decimals`).  
        /// **[sortable]**
        /// </summary>
        [JsonPropertyName("balance")]
        public string Balance { get; set; }

        /// <summary>
        /// Total number of transfers, affecting the token balance.  
        /// **[sortable]**
        /// </summary>
        [JsonPropertyName("transferCount")]
        public int TransfersCount { get; set; }

        /// <summary>
        /// Level of the block where the token balance was first changed.  
        /// **[sortable]**
        /// </summary>
        [JsonPropertyName("firstLevel")]
        public int FirstLevel { get; set; }

        /// <summary>
        /// Timestamp of the block where the token balance was first changed.
        /// </summary>
        [JsonPropertyName("firstTime")]
        public DateTime FirstTime { get; set; }

        /// <summary>
        /// Level of the block where the token balance was last changed.  
        /// **[sortable]**
        /// </summary>
        [JsonPropertyName("lastLevel")]
        public int LastLevel { get; set; }

        /// <summary>
        /// Timestamp of the block where the token balance was last changed.
        /// </summary>
        [JsonPropertyName("lastTime")]
        public DateTime LastTime { get; set; }

        public TokenBalance ToTokenBalance()
        {
            var token = Token.ToToken();
            return new TokenBalance()
            {
                Contract = token.Contract,
                TokenId = token.TokenId,
                Name = token.Name,
                Symbol = token.Symbol,
                Decimals = token.Decimals,
                Description = token.Description,
                ArtifactUri = token.ArtifactUri,
                DisplayUri = token.DisplayUri,
                ThumbnailUri = token.ThumbnailUri,
                Creators = token.Creators,
                Balance = Balance,
            };
        }
    }
}