using System;
using System.Collections.Generic;
using System.Text;

namespace Atomex.Wallet
{
    public class BalanceChangedEventArgs
    {
        public string? Currency { get; init; }
        public string? Address { get; init; }
        public string? TokenContract { get; init; }
        public int? TokenId { get; init; }
        
        //public bool IsTokenUpdate

        public static BalanceChangedEventArgs ForCurrency(string currency)
        {
            return new BalanceChangedEventArgs
            {
                Currency = currency ?? throw new ArgumentNullException(nameof(currency))
            };
        }

        public static BalanceChangedEventArgs ForCurrencyOnAddress(string currency, string address)
        {
            return new BalanceChangedEventArgs
            {
                Currency = currency ?? throw new ArgumentNullException(nameof(currency)),
                Address = address ?? throw new ArgumentNullException(nameof(address))
            };
        }

        //public static BalanceChangedEventArgs ForTokens(string tokenContract, string tokenId)
        //{
        //    return new BalanceChangedEventArgs
        //    {
        //        TokenContract = tokenContract ?? throw new ArgumentNullException(nameof(tokenContract)),
        //        Address = address ?? throw new ArgumentNullException(nameof(address))
        //    };
        //}

        //public BalanceChangedEventArgs(string currency, string address)
        //{
        //    Currency = currency ?? throw new ArgumentNullException(nameof(currency));
        //    Address = address ?? throw new ArgumentNullException(nameof(address));
        //}
    }
}