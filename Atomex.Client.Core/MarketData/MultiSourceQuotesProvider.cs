using System;
using System.Threading;
using System.Threading.Tasks;

using Atomex.MarketData.Abstract;

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
            foreach(var provider in _providers)
            {
                await provider
                    .UpdateAsync(cancellation)
                    .ConfigureAwait(false);
            }
        }
    }
}