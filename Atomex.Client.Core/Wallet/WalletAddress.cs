using System;
using System.Numerics;

using Atomex.Blockchain;

namespace Atomex.Core
{
    public enum WalletAddressUsageType
    {
        InUse,
        NoLongerUsed,
        SingleUse
    }

    public class WalletAddress
    {
        public string Id => GetId(Address, Currency, TokenBalance?.Contract, TokenBalance?.TokenId);
        public string Currency { get; set; }
        public string Address { get; set; }
        public BigInteger Balance { get; set; }
        public BigInteger UnconfirmedIncome { get; set; }
        public BigInteger UnconfirmedOutcome { get; set; }
        public string KeyPath { get; set; }
        public uint KeyIndex { get; set; }
        public int KeyType { get; set; }
        public bool HasActivity { get; set; }
        public TokenBalance TokenBalance { get; set; }
        public DateTime LastSuccessfullUpdate { get; set; }
        public WalletAddressUsageType UsageType { get; set; }

        public BigInteger AvailableBalance() => Currencies.IsBitcoinBased(Currency)
            ? Balance + UnconfirmedIncome
            : Balance;

        public static string GetId(string address, string currency, string tokenContract = null, BigInteger? tokenId = null) => 
            tokenContract == null && tokenId == null
                ? $"{address}:{currency}"
                : $"{address}:{currency}:{tokenContract}:{tokenId.Value}";
    }
}