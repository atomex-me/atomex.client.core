using System.Collections.Generic;

using Serilog;

using Atomex.Core;
using Atomex.MarketData.Abstract;

namespace Atomex.MarketData
{
    public class MarketDataRepository : IMarketDataRepository
    {
        private readonly IDictionary<string, MarketDataOrderBook> _orderBooks;
        private readonly IDictionary<string, Queue<Entry>> _entriesQueue;
        private readonly IDictionary<string, Quote> _lastQuotes;

        public MarketDataRepository(IEnumerable<Symbol> symbols)
        {
            _orderBooks = new Dictionary<string, MarketDataOrderBook>();
            _entriesQueue = new Dictionary<string, Queue<Entry>>();
            _lastQuotes = new Dictionary<string, Quote>();

            foreach (var symbol in symbols)
            {
                _orderBooks.Add(symbol.Name, new MarketDataOrderBook(symbol.Name));
                _entriesQueue.Add(symbol.Name, new Queue<Entry>());
                _lastQuotes.Add(symbol.Name, null);
            }
        }

        public void ApplyQuotes(IList<Quote> quotes)
        {
            foreach (var quote in quotes)
                if (_lastQuotes.ContainsKey(quote.Symbol))
                    _lastQuotes[quote.Symbol] = quote;
        }

        public void ApplyEntries(IList<Entry> entries)
        {
            foreach (var entry in entries)
            {
                var symbolId = entry.Symbol;
                var orderBook = OrderBookBySymbol(symbolId);

                if (orderBook == null) {
                    Log.Warning("Order book not found for symbols with id {@id}", symbolId);
                    continue;
                }

                lock (orderBook)
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
            var symbolId = snapshot.Symbol;
            var orderBook = OrderBookBySymbol(symbolId);

            if (orderBook == null) {
                Log.Warning("Order book not found for symbols with id {@id}", symbolId);
                return;
            }

            lock (orderBook)
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

        public MarketDataOrderBook OrderBookBySymbol(string symbol)
        {
            return _orderBooks.TryGetValue(symbol, out var orderBook)
                ? orderBook
                : null;
        }

        public Quote QuoteBySymbol(string symbol)
        {
            return _lastQuotes.TryGetValue(symbol, out var quote)
                ? quote
                : null;
        }
    }
}