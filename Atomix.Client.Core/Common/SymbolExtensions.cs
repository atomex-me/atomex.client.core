using System;
using Atomix.Core;
using Atomix.Core.Entities;

namespace Atomix.Common
{
    public static class SymbolExtensions
    {
        public static Side OrderSideForBuyCurrency(this Symbol symbol, Currency currency)
        {
            if (symbol.IsBaseCurrency(currency))
                return Side.Buy;
            if (symbol.IsQuoteCurrency(currency))
                return Side.Sell;

            throw new ArgumentException($"Symbol {symbol.Name} not contains currency {currency.Name}");
        }

        public static Currency PurchasedCurrency(this Symbol symbol, Side side)
        {
            return side == Side.Buy
                ? symbol.Base
                : symbol.Quote;
        }

        public static Currency SoldCurrency(this Symbol symbol, Side side)
        {
            return side == Side.Buy
                ? symbol.Quote
                : symbol.Base;
        }

        public static bool IsBaseCurrency(this Symbol symbol, Currency currency)
        {
            return symbol.Base.Name.Equals(currency.Name);
        }

        public static bool IsQuoteCurrency(this Symbol symbol, Currency currency)
        {
            return symbol.Quote.Name.Equals(currency.Name);
        }
    }
}