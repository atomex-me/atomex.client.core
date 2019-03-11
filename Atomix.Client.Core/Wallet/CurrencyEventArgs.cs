using System;
using Atomix.Core.Entities;

namespace Atomix.Wallet
{
    public class CurrencyEventArgs : EventArgs
    {
        public Currency Currency { get; set; }

        public CurrencyEventArgs(Currency currency)
        {
            Currency = currency;
        }
    }
}