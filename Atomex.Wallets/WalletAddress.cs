﻿namespace Atomex.Wallets
{
    public class WalletAddress
    {
        public string Id => Address;
        public string Currency { get; set; }
        public string Address { get; set; }
        public Balance Balance { get; set; }
        public int WalletId { get; set; }
        public string KeyPath { get; set; }
        public uint KeyIndex { get; set; }
        public bool HasActivity { get; set; }
        public long Counter { get; set; }
        public bool IsNoLongerUsed { get; set; }
    }
}