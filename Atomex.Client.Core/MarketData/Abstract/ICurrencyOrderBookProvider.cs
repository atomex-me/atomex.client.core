using System;

namespace Atomex.MarketData.Abstract
{
    public class OrderBookEventArgs : EventArgs
    {
        public MarketDataOrderBook OrderBook { get; }

        public OrderBookEventArgs(MarketDataOrderBook orderBook)
        {
            OrderBook = orderBook;
        }
    }

    public interface ICurrencyOrderBookProvider
    {
        event EventHandler<OrderBookEventArgs> OrderBookUpdated;
        event EventHandler AvailabilityChanged;

        DateTime LastUpdateTime { get; }
        bool IsAvailable { get; }
        bool IsRestart { get; }

        void Start();
        void Stop();
        MarketDataOrderBook GetOrderBook(string currency, string baseCurrency);
    }
}