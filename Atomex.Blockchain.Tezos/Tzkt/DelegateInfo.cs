using System.Text.Json.Serialization;

namespace Atomex.Blockchain.Tezos.Tzkt
{
    public class DelegateInfo
    {
        /// <summary>
        /// Name of the baking service
        /// </summary>
        [JsonPropertyName("alias")]
        public string Alias { get; set; }

        /// <summary>
        /// Public key hash of the delegate (baker)
        /// </summary>
        [JsonPropertyName("address")]
        public string Address { get; set; }

        /// <summary>
        /// Delegation status (`true` - active, `false` - deactivated)
        /// </summary>
        [JsonPropertyName("active")]
        public bool Active { get; set; }
    }
}