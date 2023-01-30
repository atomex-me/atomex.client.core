using System;
using System.Numerics;

using Atomex.Blockchain;
using Atomex.Wallet.Abstract;

namespace Atomex.Core
{
    public class WalletAddress
    {
        public string Id => Currency != "FA12" && Currency != "FA2"
            ? GetId(Address, Currency)
            : $"{Address}:{Currency}:{TokenBalance.Contract}:{TokenBalance.TokenId}";

        public string Currency { get; set; }
        public string Address { get; set; }
        public BigInteger Balance { get; set; }
        public BigInteger UnconfirmedIncome { get; set; }
        public BigInteger UnconfirmedOutcome { get; set; }
        public KeyIndex KeyIndex { get; set; }
        public bool HasActivity { get; set; }
        public int KeyType { get; set; }
        public TokenBalance TokenBalance { get; set; }
        public DateTime LastSuccessfullUpdate { get; set; }

        public BigInteger AvailableBalance() => Currencies.IsBitcoinBased(Currency)
            ? Balance + UnconfirmedIncome
            : Balance;

        public static string GetId(string address, string currency) => $"{address}:{currency}";
    }
}