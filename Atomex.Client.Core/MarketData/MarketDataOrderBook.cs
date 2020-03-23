using System;
using System.Collections.Generic;
using System.Linq;
using Atomex.Common;
using Atomex.Core;

namespace Atomex.MarketData
{
    public class MarketDataOrderBook
    {
        public const int DefaultSnapshotSize = 20;

        private readonly string _symbol;
        private long _lastTransactionId;
        public readonly SortedDictionary<decimal, Entry> Buys;
        public readonly SortedDictionary<decimal, Entry> Sells;

        public bool IsReady { get; set; }
        public object SyncRoot { get; } = new object();

        public MarketDataOrderBook(string symbol)
        {
            _symbol = symbol ?? throw new ArgumentNullException(nameof(symbol));
            _lastTransactionId = 0;

            Buys = new SortedDictionary<decimal, Entry>(new DescendingComparer<decimal>());
            Sells = new SortedDictionary<decimal, Entry>();

            IsReady = false;
        }

        public MarketDataOrderBook()
        {
            _lastTransactionId = 0;

            Buys = new SortedDictionary<decimal, Entry>(new DescendingComparer<decimal>());
            Sells = new SortedDictionary<decimal, Entry>();

            IsReady = false;
        }

        public Quote TopOfBook()
        {
            var quote = new Quote
            {
                Symbol = _symbol,
                TimeStamp = DateTime.UtcNow, // todo: change to last update time
                Bid = Buys.Count != 0 ? Buys.First().Key : 0,
                Ask = Sells.Count != 0 ? Sells.First().Key : decimal.MaxValue
            };
            return quote;
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

        public (decimal, decimal) EstimateOrderPrices(
            Side side,
            decimal amount,
            decimal amountDigitsMultiplier,
            decimal qtyDigitsMultiplier)
        {
            var requiredAmount = amount;

            lock (SyncRoot)
            {
                var book = side == Side.Buy
                    ? Sells
                    : Buys;

                if (amount == 0)
                    return book.Any()
                        ? (book.First().Key, book.First().Key)
                        : (0m, 0m);

                var totalUsedQuoteAmount = 0m;
                var totalUsedQty = 0m;

                foreach (var entryPair in book)
                {
                    var qty = entryPair.Value.Qty();
                    var price = entryPair.Key;

                    var availableAmount = AmountHelper.QtyToAmount(side, qty, price, amountDigitsMultiplier);

                    var usedAmount = Math.Min(requiredAmount, availableAmount);
                    var usedQty = AmountHelper.AmountToQty(side, usedAmount, price, qtyDigitsMultiplier);

                    totalUsedQuoteAmount += usedQty * price;
                    totalUsedQty += usedQty;

                    requiredAmount -= usedAmount;

                    if (requiredAmount <= 0)
                        return (price, totalUsedQuoteAmount / totalUsedQty);
                }
            }

            return (0m, 0m);
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

                decimal quoteQty = 0;

                foreach (var entryPair in book)
                {
                    var availiableQty = entryPair.Value.Qty();

                    if (availiableQty >= qtyToFill)
                        return (quoteQty + qtyToFill * entryPair.Key) / qty;

                    quoteQty += availiableQty * entryPair.Key;

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
                    var availiableQuoteQty = entryPair.Value.Qty() * entryPair.Key;

                    if (availiableQuoteQty >= baseQtyToFill)
                        return baseQty / (qty + baseQtyToFill / entryPair.Key);

                    qty += availiableQuoteQty / entryPair.Key;

                    baseQtyToFill -= availiableQuoteQty;
                }
            }

            return 0m;
        }

        public decimal EstimateMaxAmount(Side side, long digitsMultiplier)
        {
            var amount = 0m;

            lock (SyncRoot)
            {
                var book = side == Side.Buy
                    ? Sells
                    : Buys;

                foreach (var entryPair in book)
                {
                    amount += AmountHelper.QtyToAmount(side, entryPair.Value.Qty(), entryPair.Key, digitsMultiplier);
                }
            }

            return amount;
        }
    }
}