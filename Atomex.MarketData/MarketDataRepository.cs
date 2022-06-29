using System;
using System.Collections.Generic;
using System.Threading;

using Atomex.MarketData.Abstract;
using Atomex.MarketData.Common;
using Atomex.MarketData.Entities;

namespace Atomex.MarketData
{
    public class MarketDataRepository : IMarketDataRepository
    {
        public event EventHandler<QuotesEventArgs> QuotesUpdated;
        public event EventHandler<EntriesEventArgs> EntriesUpdated;
        public event EventHandler<SnapshotEventArgs> SnapshotUpdated;

        private readonly SemaphoreSlim _semaphoreSlim = new(1, 1);
        private readonly IDictionary<string, OrderBook> _orderBooks;
        private readonly IDictionary<string, Queue<Entry>> _entriesQueue;
        private readonly IDictionary<string, Quote> _lastQuotes;

        public MarketDataRepository()
        {
            _orderBooks = new Dictionary<string, OrderBook>();
            _entriesQueue = new Dictionary<string, Queue<Entry>>();
            _lastQuotes = new Dictionary<string, Quote>();
        }

        public void Clear()
        {
            foreach (var pair in _orderBooks)
                pair.Value.Clear();

            _orderBooks.Clear();

            foreach (var pair in _entriesQueue)
                pair.Value.Clear();

            _entriesQueue.Clear();

            _lastQuotes.Clear();
        }

        public void ApplyQuotes(IList<Quote> quotes)
        {
            foreach (var quote in quotes)
                if (_lastQuotes.ContainsKey(quote.Symbol))
                    _lastQuotes[quote.Symbol] = quote;

            QuotesUpdated?.Invoke(this, new QuotesEventArgs(quotes));
        }

        public void ApplyEntries(IList<Entry> entries)
        {
            foreach (var entry in entries)
            {
                var symbolId = entry.Symbol;

                try
                {
                    _semaphoreSlim.Wait();

                    var orderBook = OrderBookBySymbol(symbolId);

                    if (orderBook.IsReady)
                    {
                        orderBook.ApplyEntry(entry, true);
                    }
                    else
                    {
                        if (!_entriesQueue.TryGetValue(symbolId, out var queue))
                        {
                            queue = new Queue<Entry>();
                            _entriesQueue.Add(symbolId, queue);
                        }

                        queue.Enqueue(entry);
                    }
                }
                finally
                {
                    _semaphoreSlim.Release();
                }
            }

            EntriesUpdated?.Invoke(this, new EntriesEventArgs(entries));
        }

        public void ApplySnapshot(Snapshot snapshot)
        {
            var symbolId = snapshot.Symbol;

            try
            {
                _semaphoreSlim.Wait();

                var orderBook = OrderBookBySymbol(symbolId);

                orderBook.ApplySnapshot(snapshot);

                if (!_entriesQueue.TryGetValue(symbolId, out var queue))
                {
                    queue = new Queue<Entry>();
                    _entriesQueue.Add(symbolId, queue);
                }
                
                foreach (var entry in queue)
                    orderBook.ApplyEntry(entry, true);

                queue.Clear();
                orderBook.IsReady = true;
            }
            finally
            {
                _semaphoreSlim.Release();
            }

            SnapshotUpdated?.Invoke(this, new SnapshotEventArgs(snapshot));
        }

        public OrderBook OrderBookBySymbol(string symbol)
        {
            if (!_orderBooks.TryGetValue(symbol, out var orderBook))
            {
                orderBook = new OrderBook(symbol);

                _orderBooks.Add(symbol, orderBook);
            }

            return orderBook;
        }

        public Quote QuoteBySymbol(string symbol) =>
            _lastQuotes.TryGetValue(symbol, out var quote)
                ? quote
                : null;
    }
}