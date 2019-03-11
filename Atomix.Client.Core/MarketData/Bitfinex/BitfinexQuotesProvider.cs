using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Atomix.Core.Entities;
using Atomix.MarketData.Abstract;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Serilog;

namespace Atomix.MarketData.Bitfinex
{
    public class BitfinexQuotesProvider : QuotesProvider
    {
        public const string Usd = "USD";
        public string BaseUrl { get; } = "https://api.bitfinex.com/v1/";

        public BitfinexQuotesProvider(params string[] symbols)
        {
            Quotes = symbols.ToDictionary(s => s, s => new Quote());
        }

        public BitfinexQuotesProvider(IEnumerable<Currency> currencies, string baseCurrency)
        {
            Quotes = currencies.ToDictionary(currency => $"{currency.Name}{baseCurrency}".ToUpper(), currency => new Quote());
        }

        public override Quote GetQuote(string currency, string baseCurrency)
        {
            return Quotes.TryGetValue($"{currency}{baseCurrency}".ToUpper(), out var rate) ? rate : null;
        }

        protected override async Task UpdateAsync(CancellationToken cancellation = default(CancellationToken))
        {
            Log.Debug("Start of update");

            var client = new HttpClient { BaseAddress = new Uri(BaseUrl) };
            var isAvailable = true;

            try
            {
                foreach (var symbol in Quotes.Keys.ToList())
                {
                    var request = $"pubticker/{symbol.ToLower()}";

                    Log.Debug("Send request: {@request}", request);

                    var response = await client
                        .GetAsync(request, cancellation)
                        .ConfigureAwait(false);

                    if (response.IsSuccessStatusCode)
                    {
                        var responseContent = await response.Content
                            .ReadAsStringAsync()
                            .ConfigureAwait(false);

                        Log.Verbose("Raw response content: {@content}", responseContent);

                        var data = JsonConvert.DeserializeObject<JObject>(responseContent);

                        Quotes[symbol] = new Quote
                        {
                            Bid = data["bid"].Value<decimal>(),
                            Ask = data["ask"].Value<decimal>()
                        };
                    }
                    else
                    {
                        Log.Error("Invalid response code: {@code}", response.StatusCode);
                        isAvailable = false;
                    }
                }

                Log.Debug("Update finished");
            }
            catch (Exception e)
            {
                Log.Error(e, e.Message);

                isAvailable = false;
            }

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