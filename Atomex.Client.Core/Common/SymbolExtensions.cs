using System.Collections.Generic;
using System.Linq;

using Atomex.Core;
using Atomex.Wallets.Abstract;

namespace Atomex.Common
{
    public static class SymbolExtensions
    {
        public static Side OrderSideForBuyCurrency(this Symbol symbol, string currency) =>
            symbol.Name.OrderSideForBuyCurrency(currency);

        public static Side OrderSideForBuyCurrency(this Symbol symbol, CurrencyConfig currency) =>
            symbol.Name.OrderSideForBuyCurrency(currency.Name);

        public static string PurchasedCurrency(this Symbol symbol, Side side) =>
            side == Side.Buy
                ? symbol.Base
                : symbol.Quote;

        public static string SoldCurrency(this Symbol symbol, Side side) =>
            side == Side.Buy
                ? symbol.Quote
                : symbol.Base;

        public static bool IsBaseCurrency(this Symbol symbol, string currency) =>
            symbol.Base == currency;

        public static bool IsQuoteCurrency(this Symbol symbol, string currency) =>
            symbol.Quote == currency;

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
            CurrencyConfig from,
            CurrencyConfig to)
        {
            return SymbolByCurrencies(symbols, from.Name, to.Name);
        }
    }
}