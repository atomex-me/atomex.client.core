using System.Collections.Generic;

using Atomex.Core;

namespace Atomex.MarketData.Abstract
{
    public interface IMarketDataRepository
    {
        void Initialize(IEnumerable<Symbol> symbols);
        void Clear();
        void ApplyQuotes(IList<Quote> quotes);
        void ApplyEntries(IList<Entry> entries);
        void ApplySnapshot(Snapshot snapshot);
        MarketDataOrderBook OrderBookBySymbol(string symbol);
        Quote QuoteBySymbol(string symbol);
    }
}