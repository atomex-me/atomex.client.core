using System;

using Atomex.MarketData.Common;

namespace Atomex.MarketData.Abstract
{
    public interface IOrderBookProvider
    {
        event EventHandler<OrderBookEventArgs> OrderBookUpdated;
        event EventHandler AvailabilityChanged;

        DateTime LastUpdateTime { get; }
        bool IsAvailable { get; }
        string Name { get; }

        void Start();
        void Stop();
        OrderBook GetOrderBook(string currency, string quoteCurrency);
    }
}