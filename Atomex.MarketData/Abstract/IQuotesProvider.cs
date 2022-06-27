using System;

using Atomex.MarketData.Entities;

namespace Atomex.MarketData.Abstract
{
    public interface IQuotesProvider
    {
        event EventHandler QuotesUpdated;
        event EventHandler AvailabilityChanged;

        DateTime LastUpdateTime { get; }
        DateTime LastSuccessUpdateTime { get; }
        bool IsAvailable { get; }

        void Start();
        void Stop();
        Quote GetQuote(string currency, string baseCurrency);
        Quote GetQuote(string symbol);
    }
}