using System;

namespace Atomex.MarketData.Common
{
    public class OrderBookEventArgs : EventArgs
    {
        public OrderBook OrderBook { get; }

        public OrderBookEventArgs(OrderBook orderBook)
        {
            OrderBook = orderBook;
        }
    }
}