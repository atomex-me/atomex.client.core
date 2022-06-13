using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Serilog;

using Atomex.Common;
using Atomex.Core;
using Atomex.MarketData.Abstract;

namespace Atomex.MarketData.Bitfinex
{
    public class BitfinexQuotesProvider : QuotesProvider
    {
        private readonly Dictionary<string, string> QuoteSymbols = new()
        {
            { "BTCUSD", "tBTCUSD" },
            { "LTCUSD", "tLTCUSD" },
            { "ETHUSD", "tETHUSD" },
            { "XTZUSD", "tXTZUSD" },
            { "USDTUSD", "tUSTUSD" },
            { "TZBTCUSD", "tBTCUSD" },
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

        public BitfinexQuotesProvider(IEnumerable<CurrencyConfig> currencies, string baseCurrency)
        {
            Quotes = currencies
                .Select(c => QuoteSymbols[$"{c.Name}{baseCurrency}"])
                .Distinct()
                .ToDictionary(currency => currency, currency => new Quote());
        }

        public override Quote GetQuote(string currency, string baseCurrency)
        {
            if (QuoteSymbols.TryGetValue($"{currency}{baseCurrency}", out var symbol))
                return Quotes.TryGetValue(symbol, out var rate) ? rate : null;
            else return null;
        }

        public override Quote GetQuote(string symbol)
        {
            if (QuoteSymbols.TryGetValue(symbol.Replace("/", ""), out var s))
                return Quotes.TryGetValue(s, out var rate) ? rate : null;
            else return null;
        }

        protected override async Task UpdateAsync(
            CancellationToken cancellationToken = default)
        {
            var isAvailable = await UpdateQuotesAsync(cancellationToken)
                .ConfigureAwait(false);

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

        private async Task<bool> UpdateQuotesAsync(
            CancellationToken cancellationToken = default)
        {
            try
            {
                Log.Debug("Start of update");

                var symbols = string.Join(",", Quotes.Select(q => q.Key));

                var request = $"tickers/?symbols={symbols}";

                using var response = await HttpHelper.GetAsync(
                        baseUri: BaseUrl,
                        relativeUri: request,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                    return false;

                var responseContent = response.Content
                    .ReadAsStringAsync()
                    .WaitForResult();

                var tickers = JsonConvert.DeserializeObject<JArray>(responseContent);

                foreach (var tickerToken in tickers)
                {
                    if (tickerToken is not JArray ticker)
                        continue;

                    var symbol = ticker[0].Value<string>();

                    var bid = ticker[1].Value<decimal>();
                    var ask = ticker[3].Value<decimal>();
                    var dailyChangePercent = ticker[6].Value<decimal>();

                    Quotes[symbol] = new Quote
                    {
                        Bid = bid,
                        Ask = ask,
                        DailyChangePercent = dailyChangePercent
                    };
                }

                Log.Debug("Update finished");

                return true;
            }
            catch (Exception e)
            {
                Log.Error(e, e.Message);

                return false;
            }
        }
    }
}