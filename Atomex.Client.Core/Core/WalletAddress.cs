using System;

using Atomex.Blockchain.Tezos;
using Atomex.Wallet.Abstract;

namespace Atomex.Core
{
    public class WalletAddress
    {
        public string Id => Currency != "FA12" && Currency != "FA2"
            ? $"{Address}:{Currency}"
            : $"{Address}:{Currency}:{TokenBalance.Contract}:{TokenBalance.TokenId}";

        public string Currency { get; set; }
        public string Address { get; set; }
        public decimal Balance { get; set; }
        public decimal UnconfirmedIncome { get; set; }
        public decimal UnconfirmedOutcome { get; set; }
        public KeyIndex KeyIndex { get; set; }
        public bool HasActivity { get; set; }
        public int KeyType { get; set; }
        public TokenBalance TokenBalance { get; set; }
        public DateTime LastSuccessfullUpdate { get; set; }

        public decimal AvailableBalance() => Currencies.IsBitcoinBased(Currency)
            ? Balance + UnconfirmedIncome
            : Balance;
    }
}