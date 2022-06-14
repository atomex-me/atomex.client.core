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
            { "KUSDUSD", "tUSTUSD" },
            { "USDT-XTZUSD", "tUSTUSD" },
        };

        public const string Usd = "USD";

        //private string BaseUrl { get; } = "https://api.bitfinex.com/v2/";
        private string BaseUrl { get; } = "https://test.atomex.me/";

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
            return Quotes.TryGetValue(currency?.ToLower() ?? string.Empty, out var tokenRate) ? tokenRate : null;
        }

        public override Quote GetQuote(string symbol)
        {
            if (QuoteSymbols.TryGetValue(symbol.Replace("/", ""), out var s))
                return Quotes.TryGetValue(s, out var rate) ? rate : null;
            return Quotes.TryGetValue(symbol?.ToLower() ?? string.Empty, out var tokenRate) ? tokenRate : null;
        }


        protected override async Task UpdateAsync(
            CancellationToken cancellationToken = default)
        {
            Log.Debug("Start of update");

            bool isAvailable;

            try
            {
                var symbols = string.Join(",", Quotes
                    .Where(q => !q.Value.IsToken)
                    .Select(q => q.Key));
                
                var bitfinexTask = HttpHelper.GetAsync(
                    baseUri: BaseUrl,
                    requestUri: $"v2/tickers/?symbols={symbols}",
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

                        return true;
                    },
                    cancellationToken: cancellationToken);
                
                var tezToolsTask = HttpHelper.GetAsync(
                    baseUri: BaseUrl,
                    requestUri: "token/prices",
                    responseHandler: response =>
                    {
                        if (!response.IsSuccessStatusCode)
                            return false;

                        var responseContent = response.Content
                            .ReadAsStringAsync()
                            .WaitForResult();

                        var data = JsonConvert.DeserializeObject<JObject>(responseContent);

                        if (data["contracts"] is not JArray contracts) return false;

                        foreach (var token in contracts)
                        {
                            try
                            {
                                var symbol = token["symbol"]?.Value<string>();
                                if (symbol == null)
                                    continue;
                            
                                var bid = token["usdValue"]!.Value<decimal>();
                                var ask = token["usdValue"]!.Value<decimal>();

                                Quotes[symbol.ToLower()] = new Quote
                                {
                                    Bid = bid,
                                    Ask = ask,
                                    IsToken = true
                                };
                            }
                            catch
                            {
                                Log.Error("Can't update tezos tokens quotes");
                            }
                        }

                        return true;
                    },
                    cancellationToken: cancellationToken);
                
                var result = await Task.WhenAll(bitfinexTask, tezToolsTask)
                    .ConfigureAwait(false);

                isAvailable = result.All(r => r);

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