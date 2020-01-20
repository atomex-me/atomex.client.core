using System.Collections.Generic;
using Atomex.Core;
using Atomex.MarketData.Abstract;
using Serilog;

namespace Atomex.MarketData
{
    public class MarketDataRepository : IMarketDataRepository
    {
        private readonly IDictionary<int, MarketDataOrderBook> _orderBooks;
        private readonly IDictionary<int, Queue<Entry>> _entriesQueue;
        private readonly IDictionary<int, Quote> _lastQuotes;

        public MarketDataRepository(IEnumerable<Symbol> symbols)
        {
            _orderBooks = new Dictionary<int, MarketDataOrderBook>();
            _entriesQueue = new Dictionary<int, Queue<Entry>>();
            _lastQuotes = new Dictionary<int, Quote>();

            foreach (var symbol in symbols)
            {
                _orderBooks.Add(symbol.Id, new MarketDataOrderBook(symbol));
                _entriesQueue.Add(symbol.Id, new Queue<Entry>());
                _lastQuotes.Add(symbol.Id, null);
            }
        }

        public void ApplyQuotes(IList<Quote> quotes)
        {
            foreach (var quote in quotes)
                if (_lastQuotes.ContainsKey(quote.SymbolId))
                    _lastQuotes[quote.SymbolId] = quote;
        }

        public void ApplyEntries(IList<Entry> entries)
        {
            foreach (var entry in entries)
            {
                var symbolId = entry.SymbolId;
                var orderBook = OrderBookBySymbolId(symbolId);

                if (orderBook == null) {
                    Log.Warning("Order book not found for symbols with id {@id}", symbolId);
                    continue;
                }

                lock (orderBook.SyncRoot)
                {
                    if (orderBook.IsReady) {
                        orderBook.ApplyEntry(entry, true);
                    } else if (_entriesQueue.TryGetValue(symbolId, out var queue)) {
                        queue.Enqueue(entry);
                    }
                }
            }
        }

        public void ApplySnapshot(Snapshot snapshot)
        {
            var symbolId = snapshot.SymbolId;
            var orderBook = OrderBookBySymbolId(symbolId);

            if (orderBook == null) {
                Log.Warning("Order book not found for symbols with id {@id}", symbolId);
                return;
            }

            lock (orderBook.SyncRoot)
            {
                orderBook.ApplySnapshot(snapshot);

                if (_entriesQueue.TryGetValue(symbolId, out var queue))
                {
                    foreach (var entry in queue)
                        orderBook.ApplyEntry(entry, true);

                    queue.Clear();
                    orderBook.IsReady = true;
                }
            }
        }

        public MarketDataOrderBook OrderBookBySymbolId(int symbolId)
        {
            return _orderBooks.TryGetValue(symbolId, out var orderBook)
                ? orderBook
                : null;
        }

        public Quote QuoteBySymbolId(int symbolId)
        {
            return _lastQuotes.TryGetValue(symbolId, out var quote)
                ? quote
                : null;
        }
    }
}