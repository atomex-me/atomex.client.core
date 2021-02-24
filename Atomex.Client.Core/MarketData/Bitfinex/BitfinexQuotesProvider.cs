using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Atomex.Common;
using Atomex.Core;
using Atomex.MarketData.Abstract;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Serilog;

namespace Atomex.MarketData.Bitfinex
{
    public class BitfinexQuotesProvider : QuotesProvider
    {
        private readonly Dictionary<string, string> QuoteSymbols = new Dictionary<string, string>()
        {
            { "BTCUSD", "tBTCUSD" },
            { "LTCUSD", "tLTCUSD" },
            { "ETHUSD", "tETHUSD" },
            { "XTZUSD", "tXTZUSD" },
            { "USDTUSD", "tUSTUSD" },
            { "TZBTCUSD", "tBTCUSD" },
            { "NYXUSD", "tBTCUSD" },
            { "FA2USD", "tUSTUSD" },
            { "TBTCUSD", "tBTCUSD" },
            { "WBTCUSD", "tBTCUSD" },
            { "KUSDUSD", "tUSTUSD" }
        };

        public const string Usd = "USD";

        //private string BaseUrl { get; } = "https://api.bitfinex.com/v2/";
        private string BaseUrl { get; } = "https://test.atomex.me/v2/";

        public BitfinexQuotesProvider(params string[] symbols) //todo: check before use
        {
            Quotes = symbols.ToDictionary(s => s, s => new Quote());
        }

        public BitfinexQuotesProvider(IEnumerable<Currency> currencies, string baseCurrency)
        {
            Quotes = currencies
                .Select(c => QuoteSymbols[$"{c.Name}{baseCurrency}"])
                .Distinct()
                .ToDictionary(currency => currency, currency => new Quote());
        }

        public override Quote GetQuote(string currency, string baseCurrency)
        {
            return Quotes.TryGetValue(QuoteSymbols[$"{currency}{baseCurrency}"], out var rate) ? rate : null;
        }

        public override Quote GetQuote(string symbol)
        {
            return Quotes.TryGetValue(QuoteSymbols[symbol.Replace("/", "")], out var rate) ? rate : null;
        }

        protected override async Task UpdateAsync(
            CancellationToken cancellationToken = default)
        {
            Log.Debug("Start of update");

            bool isAvailable;

            try
            {
                var symbols = string.Join(",", Quotes.Select(q => q.Key));

                var request = $"tickers/?symbols={symbols}";

                isAvailable = await HttpHelper.GetAsync(
                        baseUri: BaseUrl,
                        requestUri: request,
                        responseHandler: response =>
                        {
                            if (!response.IsSuccessStatusCode)
                                return false;

                            var responseContent = response.Content
                                .ReadAsStringAsync()
                                .WaitForResult();

                            var tickers = JsonConvert.DeserializeObject<JArray>(responseContent);

                            foreach (var tickerToken in tickers)
                            {
                                if (!(tickerToken is JArray ticker))
                                    continue;

                                var symbol = ticker[0].Value<string>();

                                var bid = ticker[1].Value<decimal>();
                                var ask = ticker[3].Value<decimal>();

                                Quotes[symbol] = new Quote
                                {
                                    Bid = bid,
                                    Ask = ask
                                };
                            }

                            return true;
                        },
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

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