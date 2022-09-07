namespace Atomex.Wallet
{
    public class BalanceChangedEventArgs
    {
        public string? Currency { get; init; }
        public string? Address { get; init; }
    }

    public class TokenBalanceChangedEventArgs : BalanceChangedEventArgs
    {
        public string? TokenContract { get; init; }
        public int? TokenId { get; init; }
    }
}