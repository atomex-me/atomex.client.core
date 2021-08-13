using System;

using Atomex.Blockchain.Tezos;
using Atomex.Wallet.Abstract;

namespace Atomex.Core
{
    public class WalletAddress
    {
        public const int MaxNumberLength = 256;

        public string UniqueId => Currency != "FA12" && Currency != "FA2"
            ? $"{Address}:{Currency}"
            : $"{Address}:{Currency}:{TokenBalance.Contract}:{TokenBalance.TokenId}";

        public long Id { get; set; }
        public string Currency { get; set; }
        public string Address { get; set; }
        public decimal Balance { get; set; }
        public decimal UnconfirmedIncome { get; set; }
        public decimal UnconfirmedOutcome { get; set; }
        public KeyIndex KeyIndex { get; set; }
        public bool HasActivity { get; set; }
        public int KeyType { get; set; }

        /// <summary>
        /// Public key in base64
        /// </summary>
        public string PublicKey { get; set; }
        /// <summary>
        /// Signature in base64
        /// </summary>
        public string ProofOfPossession { get; set; }
        public string Nonce { get; set; }

        public TokenBalance TokenBalance { get; set; }

        public byte[] PublicKeyBytes() =>
            Convert.FromBase64String(PublicKey);

        public decimal AvailableBalance(bool includeUnconfirmedIncome = false) =>
            includeUnconfirmedIncome
                ? Balance + UnconfirmedIncome + UnconfirmedOutcome
                : Balance + UnconfirmedOutcome;
        
        public override string ToString() =>
            $"{Address};{Balance};{UnconfirmedIncome};{UnconfirmedOutcome}";
        
        public WalletAddress Copy()
        {
            return (WalletAddress)MemberwiseClone();
        }
    }
}