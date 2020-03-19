using System.Collections.Generic;
using System.Linq;
using Atomex.Core;

namespace Atomex.Common
{
    public static class SymbolExtensions
    {
        public static Side OrderSideForBuyCurrency(this Symbol symbol, string currency)
        {
            return OrderSideForBuyCurrency(symbol.Name, currency);
        }

        public static Side OrderSideForBuyCurrency(this Symbol symbol, Currency currency)
        {
            return OrderSideForBuyCurrency(symbol.Name, currency.Name);
        }

        public static Side OrderSideForBuyCurrency(this string symbol, string currency)
        {
            return IsBaseCurrency(symbol, currency)
                ? Side.Buy
                : Side.Sell;
        }

        public static string PurchasedCurrency(this Symbol symbol, Side side)
        {
            return side == Side.Buy
                ? symbol.Base
                : symbol.Quote;
        }

        public static string PurchasedCurrency(this string symbol, Side side)
        {
            return side == Side.Buy
                ? symbol.BaseCurrency()
                : symbol.QuoteCurrency();
        }

        public static string SoldCurrency(this Symbol symbol, Side side)
        {
            return side == Side.Buy
                ? symbol.Quote
                : symbol.Base;
        }

        public static string SoldCurrency(this string symbol, Side side)
        {
            return side == Side.Buy
                ? symbol.QuoteCurrency()
                : symbol.BaseCurrency();
        }

        public static bool IsBaseCurrency(this Symbol symbol, string currency)
        {
            return symbol.Base == currency;
        }

        public static bool IsBaseCurrency(this string symbol, string currency)
        {
            return BaseCurrency(symbol) == currency;
        }

        public static string BaseCurrency(this string symbol)
        {
            return symbol.Substring(0, symbol.IndexOf('/'));
        }

        public static string QuoteCurrency(this string symbol)
        {
            return symbol.Substring(symbol.IndexOf('/') + 1);
        }

        public static bool IsQuoteCurrency(this Symbol symbol, string currency)
        {
            return symbol.Quote == currency;
        }

        public static Symbol SymbolByCurrencies(
            this IEnumerable<Symbol> symbols,
            string from,
            string to)
        {
            if (from == null || to == null)
                return null;

            return symbols.FirstOrDefault(s => s.Name == $"{from}/{to}" || s.Name == $"{to}/{from}");
        }

        public static Symbol SymbolByCurrencies(
            this IEnumerable<Symbol> symbols,
            Currency from,
            Currency to)
        {
            return SymbolByCurrencies(symbols, from.Name, to.Name);
        }
    }
}