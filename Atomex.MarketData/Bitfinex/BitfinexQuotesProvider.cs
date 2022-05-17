using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;
using Atomex.MarketData.Abstract;
using Atomex.MarketData.Entities;
using Atomex.Common;

namespace Atomex.MarketData.Bitfinex
{
    public class BitfinexQuotesProvider : QuotesProvider
    {
        private static readonly Dictionary<string, string> QuoteSymbols = new()
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

        private string BaseUrl { get; } = "https://api.bitfinex.com/v2/";
        private readonly ILogger _log;

        public BitfinexQuotesProvider(ILogger log = null, params string[] symbols)
        {
            _log = log;
            Quotes = symbols.ToDictionary(s => s, s => new Quote());
        }

        public BitfinexQuotesProvider(IEnumerable<string> currencies, string baseCurrency, ILogger log = null)
        {
            _log = log;
            Quotes = currencies
                .Select(c => QuoteSymbols[$"{c}{baseCurrency}"])
                .Distinct()
                .ToDictionary(currency => currency, currency => new Quote());
        }

        public override Quote GetQuote(string currency, string baseCurrency) =>
            QuoteSymbols.TryGetValue($"{currency}{baseCurrency}", out var symbol)
                ? Quotes.TryGetValue(symbol, out var rate) ? rate : null
                : null;

        public override Quote GetQuote(string symbol) =>
            QuoteSymbols.TryGetValue(symbol.Replace("/", ""), out var s)
                ? Quotes.TryGetValue(s, out var rate) ? rate : null
                : null;

        protected override async Task UpdateAsync(
            CancellationToken cancellationToken = default)
        {
            _log.LogDebug("Start of update");

            bool isAvailable;

            try
            {
                isAvailable = await UpdateQuotesAsync(cancellationToken)
                    .ConfigureAwait(false);

                _log.LogDebug("Update finished");
            }
            catch (Exception e)
            {
                _log.LogError(e, e.Message);

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

        private async Task<bool> UpdateQuotesAsync(CancellationToken cancellationToken = default)
        {
            var symbols = string.Join(",", Quotes.Select(q => q.Key));

            var request = $"tickers/?symbols={symbols}";

            var response = await HttpHelper
                .GetAsync(
                    baseUri: BaseUrl,
                    relativeUri: request,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
                return false;

            var responseContent = await response
                .Content
                .ReadAsStringAsync()
                .ConfigureAwait(false);

            using var tickers = JsonDocument.Parse(responseContent);

            if (tickers.RootElement.ValueKind != JsonValueKind.Array)
                return false;

            foreach (var tickerToken in tickers.RootElement.EnumerateArray())
            {
                if (tickerToken.ValueKind != JsonValueKind.Array)
                    continue;

                var symbol = tickerToken[0].GetString();
                var bid = tickerToken[1].GetDecimal();
                var ask = tickerToken[3].GetDecimal();
                var dailyChangePercent = tickerToken[6].GetDecimal();

                Quotes[symbol] = new Quote
                {
                    Bid = bid,
                    Ask = ask,
                    DailyChangePercent = dailyChangePercent
                };
            }

            return true;
        }
    }
}