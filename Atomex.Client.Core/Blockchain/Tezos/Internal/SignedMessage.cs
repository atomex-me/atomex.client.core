﻿using Newtonsoft.Json;

namespace Atomex.Blockchain.Tezos.Internal
{
    public class SignedMessage
    {
        public const int HashLength = 32;
        public const int HashSizeInBits = HashLength * 8;

        [JsonProperty("sig")]
        public byte[] SignedHash { get; set; }
        [JsonProperty("edsig")]
        public string EncodedSignature { get; set; }
        [JsonProperty("sbytes")]
        public string SignedBytes { get; set; }
    }
}