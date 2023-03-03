using System.Numerics;

namespace Atomex.Wallet
{
    public class BalanceChangedEventArgs
    {
        public string Currency { get; init; }
        public string[] Addresses { get; init; }
    }

    public class TokenBalanceChangedEventArgs : BalanceChangedEventArgs
    {
        public (string, BigInteger)[] Tokens { get; init; }
    }
}