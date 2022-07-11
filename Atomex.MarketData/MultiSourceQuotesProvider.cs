using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Atomex.MarketData.Abstract;
using Atomex.MarketData.Entities;
using Microsoft.Extensions.Logging;

namespace Atomex.MarketData
{
    public class MultiSourceQuotesProvider : QuotesProvider
    {
        private readonly QuotesProvider[] _providers;

        public MultiSourceQuotesProvider(ILogger? log = null, params QuotesProvider[] providers)
        {
            Log = log;
            _providers = providers ?? throw new ArgumentNullException(nameof(providers));
            
            if (_providers.Length == 0)
                throw new ArgumentException("At least one quote provider must be used");
        }

        public override Quote GetQuote(string currency, string baseCurrency)
        {
            foreach (var provider in _providers)
            {
                var quote = provider.GetQuote(currency, baseCurrency);

                if (quote != null)
                    return quote;
            }

            return null;
        }

        public override Quote GetQuote(string symbol)
        {
            foreach (var provider in _providers)
            {
                var quote = provider.GetQuote(symbol);

                if (quote != null)
                    return quote;
            }

            return null;
        }

        public override async Task UpdateAsync(CancellationToken cancellation = default)
        {
            var updateTasks = _providers.Select(p => p.UpdateAsync(cancellation));
            await Task.WhenAll(updateTasks);
            Log?.LogDebug("Update multisource quotes finished");

            var isAvailable = _providers.Any(provider => provider.IsAvailable);
            LastUpdateTime = DateTime.Now;
            
            if (isAvailable)
                LastSuccessUpdateTime = LastUpdateTime;

            if (IsAvailable != isAvailable)
            {
                IsAvailable = isAvailable;
                RiseAvailabilityChangedEvent(EventArgs.Empty);
            }

            if (IsAvailable)
                RiseQuotesUpdatedEvent(EventArgs.Empty);
        }
    }
}
