using System;
using System.Collections.Generic;

namespace Atomix.Core.Entities
{
    public class WalletAddress
    {
        public const int MaxNumberLength = 256;

        public long Id { get; set; }
        public int CurrencyId { get; set; }
        public Currency Currency { get; set; }
        public string Address { get; set; }

        /// <summary>
        /// Public key in base64
        /// </summary>
        public string PublicKey { get; set; }
        /// <summary>
        /// Signature in base64
        /// </summary>
        public string ProofOfPossession { get; set; }
        public string Nonce { get; set; }

        public IList<OrderWallet> Orders { get; } = new List<OrderWallet>();

        public byte[] PublicKeyBytes()
        {
            return Convert.FromBase64String(PublicKey);
        }
    }
}