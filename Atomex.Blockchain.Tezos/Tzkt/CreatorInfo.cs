using System.Text.Json.Serialization;

namespace Atomex.Blockchain.Tezos.Tzkt
{
    public class CreatorInfo
    {
        /// <summary>
        /// Name of the project behind the account or account description
        /// </summary>
        [JsonPropertyName("alias")]
        public string Alias { get; set; }

        /// <summary>
        /// Public key hash of the account
        /// </summary>
        [JsonPropertyName("address")]
        public string Address { get; set; }
    }
}