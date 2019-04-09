using Atomix.Cryptography;
using Newtonsoft.Json;

namespace Atomix.Blockchain.Tezos.Internal
{
    public class SignedMessage
    {
        public const int HashSizeBits = 32 * 8;

        [JsonProperty("bytes")]
        public byte[] Bytes { get; set; }
        [JsonProperty("sig")]
        public byte[] SignedHash { get; set; }
        [JsonProperty("edsig")]
        public string EncodedSignature { get; set; }
        [JsonProperty("sbytes")]
        public string SignedBytes { get; set; }

        public string HashBytes()
        {
            return Base58Check.Encode(new HmacBlake2b(HashSizeBits).ComputeHash(Bytes));
        }
    }
}