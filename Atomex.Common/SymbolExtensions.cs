namespace Atomex.Common
{
    public static class SymbolExtensions
    {
        public static Side OrderSideForBuyCurrency(this string symbol, string currency) =>
            IsBaseCurrency(symbol, currency)
                ? Side.Buy
                : Side.Sell;

        public static string PurchasedCurrency(this string symbol, Side side) =>
            side == Side.Buy
                ? symbol.BaseCurrency()
                : symbol.QuoteCurrency();

        public static string SoldCurrency(this string symbol, Side side) =>
            side == Side.Buy
                ? symbol.QuoteCurrency()
                : symbol.BaseCurrency();

        public static bool IsBaseCurrency(this string symbol, string currency) =>
            BaseCurrency(symbol) == currency;

        public static string BaseCurrency(this string symbol) =>
            symbol[..symbol.IndexOf('/')];

        public static string QuoteCurrency(this string symbol) =>
            symbol[(symbol.IndexOf('/') + 1)..];
    }
}