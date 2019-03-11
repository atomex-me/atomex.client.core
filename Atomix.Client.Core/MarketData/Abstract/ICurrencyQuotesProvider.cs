using System;

namespace Atomix.MarketData.Abstract
{
    public interface ICurrencyQuotesProvider
    {
        event EventHandler QuotesUpdated;
        event EventHandler AvailabilityChanged;

        DateTime LastUpdateTime { get; }
        DateTime LastSuccessUpdateTime { get; }
        bool IsAvailable { get; }

        void Start();
        void Stop();
        Quote GetQuote(string currency, string baseCurrency);
    }
}