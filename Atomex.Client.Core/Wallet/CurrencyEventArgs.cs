using System;

#nullable enable

namespace Atomex.Wallet
{
    [Obsolete]
    public class CurrencyEventArgs : EventArgs
    {
        public string? Currency { get; }
        public string? Address { get; }
        public string? TokenContract { get; }
        public int? TokenId { get; }
        public bool IsTokenUpdate { get; }

        public CurrencyEventArgs(string currency)
        {
            Currency = currency;
            IsTokenUpdate = false;
        }

        public CurrencyEventArgs(string? address, string? tokenContract, int? tokenId)
        {
            Address = address;
            TokenContract = tokenContract;
            TokenId = tokenId;
            IsTokenUpdate = true;
        }
    }
}