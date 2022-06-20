using Atomex.MarketData.Abstract;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Atomex.MarketData
{
    public class MultiSourceQuotesProvider : QuotesProvider
    {
        private readonly QuotesProvider[] _providers;

        public MultiSourceQuotesProvider(params QuotesProvider[] providers)
        {
            _providers = providers ?? throw new ArgumentNullException(nameof(providers));

            if (_providers.Length == 0)
                throw new ArgumentException("At least one quote provider must be used");

            SubscribeToAllProviderEvents();
        }

        public override bool IsAvailable => _providers.All(p => p.IsAvailable);

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

        public override Task UpdateAsync(CancellationToken cancellation = default)
        {
            var updateTasks = _providers.Select(p => p.UpdateAsync(cancellation));

            return Task.WhenAll(updateTasks);
        }

        private void SubscribeToAllProviderEvents()
        {
            foreach (var provider in _providers)
                SubscribeToProviderEvents(provider);
        }

        private void SubscribeToProviderEvents(QuotesProvider quotesProvider)
        {
            quotesProvider.AvailabilityChanged += OnProviderAvailabilityChanged;
            quotesProvider.QuotesUpdated += OnProviderQuotesUpdated;
        }

        private void OnProviderAvailabilityChanged(object sender, EventArgs args) => RiseAvailabilityChangedEvent(EventArgs.Empty);
        private void OnProviderQuotesUpdated(object sender, EventArgs args) => RiseQuotesUpdatedEvent(EventArgs.Empty);
    }
}
