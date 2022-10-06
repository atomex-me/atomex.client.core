using System.Collections.Generic;

namespace Atomex.Wallet
{
    public class BalanceChangedEventArgs
    {
        public string[] Currencies { get; init; }
        public string[] Addresses { get; init; }
    }

    public class TokenBalanceChangedEventArgs : BalanceChangedEventArgs
    {
        public (string, decimal)[] Tokens { get; init; }
    }
}