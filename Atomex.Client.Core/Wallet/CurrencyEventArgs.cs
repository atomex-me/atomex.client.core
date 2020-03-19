using System;
using Atomex.Core;

namespace Atomex.Wallet
{
    public class CurrencyEventArgs : EventArgs
    {
        public string Currency { get; set; }

        public CurrencyEventArgs(string currency)
        {
            Currency = currency;
        }
    }
}