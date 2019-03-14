﻿using System;
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
    }
}