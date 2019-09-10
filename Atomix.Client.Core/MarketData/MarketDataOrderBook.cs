using System;
using System.Collections.Generic;
using System.Linq;
using Atomix.Common;
using Atomix.Core;
using Atomix.Core.Entities;

namespace Atomix.MarketData
{
    public class MarketDataOrderBook
    {
        public const int DefaultSnapshotSize = 20;

        private readonly Symbol _symbol;
        private long _lastTransactionId;
        public readonly SortedDictionary<decimal, Entry> Buys;
        public readonly SortedDictionary<decimal, Entry> Sells;

        public bool IsReady { get; set; }
        public object SyncRoot { get; } = new object();

        public MarketDataOrderBook(Symbol symbol)
        {
            _symbol = symbol ?? throw new ArgumentNullException(nameof(symbol));
            _lastTransactionId = 0;

            Buys = new SortedDictionary<decimal, Entry>(new DescendingComparer<decimal>());
            Sells = new SortedDictionary<decimal, Entry>();

            IsReady = false;
        }
        
        //public MarketDataOrderBook()
        //{
        //    _lastTransactionId = 0;

        //    Buys = new SortedDictionary<decimal, Entry>(new DescendingComparer<decimal>());
        //    Sells = new SortedDictionary<decimal, Entry>();

        //    IsReady = false;
        //}

        public Quote TopOfBook()
        {
            return new Quote
            {
                SymbolId = _symbol.Id,
                TimeStamp = DateTime.UtcNow, // todo: change to last update time
                Bid = Buys.Count != 0 ? Buys.First().Key : 0,
                Ask = Sells.Count != 0 ? Sells.First().Key : decimal.MaxValue
            };
        }

        public bool IsValid()
        {
            var quote = TopOfBook();
            return quote.Bid != 0 && quote.Ask != 0 && quote.Ask != decimal.MaxValue;
        }

        public void ApplySnapshot(Snapshot snapshot)
        {
            Buys.Clear();
            Sells.Clear();

            foreach (var entry in snapshot.Entries)
                ApplyEntry(entry);

            _lastTransactionId = snapshot.LastTransactionId;
        }

        public void ApplyEntry(Entry entry, bool checkTransactionId = false)
        {
            if (checkTransactionId && entry.TransactionId <= _lastTransactionId)
                return;

            var book = entry.Side == Side.Buy ? Buys : Sells;

            if (entry.Qty() > 0) {
                book[entry.Price] = entry;
            } else {
                book.Remove(entry.Price);
            }
        }

        public void Clear()
        {
            Buys.Clear();
            Sells.Clear();
        }

        public decimal EstimatedDealPrice(Side side, decimal amount)
        {
            var amountToFill = amount;

            lock (SyncRoot)
            {
                var book = side == Side.Buy
                    ? Sells
                    : Buys;

                if (amount == 0)
                    return book.Any() ? book.First().Key : 0;

                foreach (var entryPair in book)
                {
                    var qty = entryPair.Value.Qty();
                    var availableAmount = AmountHelper.QtyToAmount(side, qty, entryPair.Key);

                    amountToFill -= availableAmount;

                    if (amountToFill <= 0)
                        return entryPair.Key;
                }
            }

            return 0m;
        }

        public decimal AverageDealBasePrice(Side side, decimal qty)
        {
            var qtyToFill = qty;

            lock (SyncRoot)
            {
                var book = side == Side.Buy
                    ? Sells
                    : Buys;

                if (qty == 0)
                    return book.Any() ? book.First().Key : 0;

                decimal baseQty = 0;

                foreach (var entryPair in book)
                {
                    var availiableQty = entryPair.Value.Qty();

                    if (availiableQty >= qtyToFill)
                        return (baseQty + qtyToFill * entryPair.Key) / qty;

                    baseQty += availiableQty * entryPair.Key;

                    qtyToFill -= availiableQty;
                }
            }

            return 0m;
        }

        public decimal AverageDealQuotePrice(Side side, decimal baseQty)
        {
            var baseQtyToFill = baseQty;

            lock (SyncRoot)
            {
                var book = side == Side.Buy
                    ? Sells
                    : Buys;

                if (baseQty == 0)
                    return book.Any() ? book.First().Key : 0;

                decimal qty = 0;

                foreach (var entryPair in book)
                {
                    var availiableBaseQty = entryPair.Value.Qty() * entryPair.Key;

                    if (availiableBaseQty >= baseQtyToFill)
                        return baseQty / (qty + baseQtyToFill / entryPair.Key);

                    qty += availiableBaseQty / entryPair.Key;

                    baseQtyToFill -= availiableBaseQty;
                }
            }

            return 0m;
        }

        public decimal EstimateMaxAmount(Side side)
        {
            var amount = 0m;

            lock (SyncRoot)
            {
                var book = side == Side.Buy
                    ? Sells
                    : Buys;

                foreach (var entryPair in book)
                {
                    amount += AmountHelper.QtyToAmount(
                        side: side,
                        qty: entryPair.Value.Qty(),
                        price: entryPair.Key);
                }
            }

            return amount;
        }
    }
}