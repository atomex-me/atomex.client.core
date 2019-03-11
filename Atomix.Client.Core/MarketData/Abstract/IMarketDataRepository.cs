using System.Collections.Generic;

namespace Atomix.MarketData.Abstract
{
    public interface IMarketDataRepository
    {
        void ApplyQuotes(IList<Quote> quotes);
        void ApplyEntries(IList<Entry> entries);
        void ApplySnapshot(Snapshot snapshot);
        MarketDataOrderBook OrderBookBySymbolId(int symbolId);
        Quote QuoteBySymbolId(int symbolId);
    }
}