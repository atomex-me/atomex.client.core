using System;
using System.Collections.Generic;

using Atomex.MarketData.Common;
using Atomex.MarketData.Entities;

namespace Atomex.MarketData.Abstract
{
    public interface IMarketDataRepository
    {
        event EventHandler<QuotesEventArgs> QuotesUpdated;
        event EventHandler<EntriesEventArgs> EntriesUpdated;
        event EventHandler<SnapshotEventArgs> SnapshotUpdated;

        void Clear();
        void ApplyQuotes(IList<Quote> quotes);
        void ApplyEntries(IList<Entry> entries);
        void ApplySnapshot(Snapshot snapshot);
        OrderBook OrderBookBySymbol(string symbol);
        Quote QuoteBySymbol(string symbol);
    }
}