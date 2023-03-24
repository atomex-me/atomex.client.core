using System;
using System.Numerics;

using Atomex.Blockchain;

namespace Atomex.Wallets
{
    public enum WalletAddressType
    {
        Active,
        Inactive,
        SingleUse
    }

    public class WalletAddress
    {
        public string Id => GetUniqueId(Address, TokenBalance?.Contract, TokenBalance?.TokenId);
        public string Address { get; set; }
        public string Currency { get; set; }
        public BigInteger Balance { get; set; }
        public BigInteger UnconfirmedIncome { get; set; }
        public BigInteger UnconfirmedOutcome { get; set; }
        public string KeyPath { get; set; }
        public uint KeyIndex { get; set; }
        public int KeyType { get; set; }
        public bool HasActivity { get; set; }
        public TokenBalance TokenBalance { get; set; }
        public DateTime LastSuccessfullUpdate { get; set; }
        public WalletAddressType Type { get; set; }

        public bool IsDisabled =>
            Type == WalletAddressType.Inactive ||
            Type == WalletAddressType.SingleUse && HasActivity && Balance == 0 && UnconfirmedIncome == 0 && UnconfirmedOutcome == 0;

        public static string GetUniqueId(string address, string tokenContract = null, BigInteger? tokenId = null) =>
            tokenContract == null && tokenId == null
                ? $"{address}"
                : $"{address}:{tokenContract}:{tokenId.Value}";
    }
}