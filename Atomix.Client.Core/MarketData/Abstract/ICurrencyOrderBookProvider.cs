using System;

namespace Atomix.MarketData.Abstract
{
    public interface ICurrencyOrderBookProvider
    {
        event EventHandler OrderBookUpdated;
        event EventHandler AvailabilityChanged;

        DateTime LastUpdateTime { get; }
        DateTime LastSuccessUpdateTime { get; }
        bool IsAvailable { get; }

        void Start();
        void Stop();
        MarketDataOrderBook GetOrderBook(string currency, string baseCurrency);
    }
}