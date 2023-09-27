using System;
using System.Collections.Generic;
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
        private List<QuotesProvider>? _providers;
        public Action<MultiSourceQuotesProvider>? ConfigureOnStart;

        public MultiSourceQuotesProvider(ILogger? log = null)
        {
            Log = log;
        }

        public void AddProviders(params QuotesProvider[] providers)
        {
            _providers ??= new List<QuotesProvider>();
            _providers.AddRange(providers);
        }

        public override void Start()
        {
            ConfigureOnStart?.Invoke(this);
            
            base.Start();
        }

        public override void Stop()
        {
            base.Stop();

            _providers?.Clear();
        }

        public override Quote? GetQuote(string currency, string baseCurrency)
        {
            if (_providers == null)
                return null;

            foreach (var provider in _providers)
            {
                var quote = provider.GetQuote(currency, baseCurrency);

                if (quote != null)
                    return quote;
            }

            return null;
        }

        public override Quote? GetQuote(string symbol)
        {
            if (_providers == null)
                return null;

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
            var updateTasks = _providers?.Select(p => p.UpdateAsync(cancellation));

            if (updateTasks != null && updateTasks.Any())
            {
                await Task.WhenAll(updateTasks);

                Log?.LogDebug("Update multisource quotes finished");
            }

            var isAvailable = _providers?.Any(provider => provider.IsAvailable) ?? false;
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
