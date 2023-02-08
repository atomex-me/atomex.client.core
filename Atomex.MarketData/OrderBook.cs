using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

using Atomex.Common;
using Atomex.MarketData.Common;
using Atomex.MarketData.Entities;

namespace Atomex.MarketData
{
    public class OrderBook
    {
        public const int DefaultSnapshotSize = 20;

        private readonly string _symbol;
        private long _lastTransactionId;
        private readonly SemaphoreSlim _semaphoreSlim = new(1, 1);

        public readonly SortedDictionary<decimal, Entry> Buys;
        public readonly SortedDictionary<decimal, Entry> Sells;
        public bool IsReady { get; set; }

        public OrderBook(string symbol)
        {
            _symbol = symbol ?? throw new ArgumentNullException(nameof(symbol));
            _lastTransactionId = 0;

            Buys = new SortedDictionary<decimal, Entry>(new DescendingComparer<decimal>());
            Sells = new SortedDictionary<decimal, Entry>();
            IsReady = false;
        }

        public Quote TopOfBook()
        {
            try
            {
                _semaphoreSlim.Wait();

                var quote = new Quote
                {
                    Symbol    = _symbol,
                    TimeStamp = DateTime.UtcNow, // todo: change to last update time
                    Bid       = Buys.Count != 0 ? Buys.First().Key : 0,
                    Ask       = Sells.Count != 0 ? Sells.First().Key : decimal.MaxValue
                };

                return quote;
            }
            finally
            {
                _semaphoreSlim.Release();
            }
        }

        public bool IsValid()
        {
            var quote = TopOfBook();
            return quote.Bid != 0 && quote.Ask != 0 && quote.Ask != decimal.MaxValue;
        }

        public void ApplySnapshot(Snapshot snapshot)
        {
            try
            {
                _semaphoreSlim.Wait();

                Buys.Clear();
                Sells.Clear();

                foreach (var entry in snapshot.Entries)
                    ApplyEntryUnsync(entry);

                _lastTransactionId = snapshot.LastTransactionId;
            }
            finally
            {
                _semaphoreSlim.Release();
            }
        }

        private void ApplyEntryUnsync(Entry entry, bool checkTransactionId = false)
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

        public void ApplyEntry(Entry entry, bool checkTransactionId = false)
        {
            try
            {
                _semaphoreSlim.Wait();

                ApplyEntryUnsync(entry, checkTransactionId);
            }
            finally
            {
                _semaphoreSlim.Release();
            }
        }

        public void Clear()
        {
            try
            {
                _semaphoreSlim.Wait();

                Buys.Clear();
                Sells.Clear();
            }
            finally
            {
                _semaphoreSlim.Release();
            }
        }

        public (decimal, decimal) EstimateOrderPrices(
            Side side,
            decimal amount,
            int amountPrecision,
            int qtyPrecision,
            AmountType amountType)
        {
            try
            {
                _semaphoreSlim.Wait();

                var requiredAmount = amount;

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

                    var availableAmount = amountType == AmountType.Sold
                        ? AmountHelper.QtyToSellAmount(side, qty, price, amountPrecision)
                        : AmountHelper.QtyToBuyAmount(side, qty, price, amountPrecision);

                    var usedAmount = Math.Min(requiredAmount, availableAmount);
                    var usedQty = amountType == AmountType.Sold
                        ? AmountHelper.AmountToSellQty(side, usedAmount, price, qtyPrecision)
                        : AmountHelper.AmountToBuyQty(side, usedAmount, price, qtyPrecision);

                    totalUsedQuoteAmount += usedQty * price;
                    totalUsedQty += usedQty;

                    requiredAmount -= usedAmount;

                    if (requiredAmount <= 0)
                        return (price, totalUsedQuoteAmount / totalUsedQty);
                }

                return (0m, 0m);
            }
            finally
            {
                _semaphoreSlim.Release();
            }
        }

        public decimal AverageDealBasePrice(Side side, decimal qty)
        {
            try
            {
                _semaphoreSlim.Wait();

                var qtyToFill = qty;

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

                return 0m;
            }
            finally
            {
                _semaphoreSlim.Release();
            }
        }

        public decimal AverageDealQuotePrice(Side side, decimal baseQty)
        {
            try
            {
                _semaphoreSlim.Wait();

                var baseQtyToFill = baseQty;

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

                return 0m;
            }
            finally
            {
                _semaphoreSlim.Release();
            }
        }

        public (decimal soldAmount, decimal purchasedAmount) EstimateMaxAmount(
            Side side,
            int precision)
        {
            try
            {
                _semaphoreSlim.Wait();

                var soldAmount = 0m;
                var purchasedAmount = 0m;

                var book = side == Side.Buy
                    ? Sells
                    : Buys;

                foreach (var entryPair in book)
                {
                    soldAmount += AmountHelper.QtyToSellAmount(side, entryPair.Value.Qty(), entryPair.Key, precision);
                    purchasedAmount += AmountHelper.QtyToBuyAmount(side, entryPair.Value.Qty(), entryPair.Key, precision);
                }

                return (soldAmount, purchasedAmount);
            }
            finally
            {
                _semaphoreSlim.Release();
            }
        }
    }
}