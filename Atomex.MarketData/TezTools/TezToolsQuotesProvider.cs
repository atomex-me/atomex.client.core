using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Microsoft.Extensions.Logging;

using Atomex.Common;
using Atomex.MarketData.Abstract;
using Atomex.MarketData.Entities;

namespace Atomex.MarketData.TezTools
{
    public class TezToolsQuotesProvider : QuotesProvider
    {
        public const string Usd = "USD";

        private string BaseUrl { get; } = "https://test.atomex.me/";
        private readonly ILogger _log;

        public TezToolsQuotesProvider(ILogger log = null)
        {
            _log = log;

            Quotes = new Dictionary<string, Quote>();
        }

        public override Quote GetQuote(string currency, string baseCurrency)
        {
            return Quotes.TryGetValue(currency?.ToLower() ?? string.Empty, out var tokenRate)
                ? tokenRate
                : null;
        }

        public override Quote GetQuote(string symbol)
        {
            return Quotes.TryGetValue(symbol?.ToLower() ?? string.Empty, out var tokenRate)
                ? tokenRate
                : null;
        }

        public override async Task UpdateAsync(
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
                var response = await HttpHelper.GetAsync(
                    baseUri: BaseUrl,
                    relativeUri: "token/prices",
                    cancellationToken: cancellationToken);

                if (!response.IsSuccessStatusCode)
                    return false;

                var responseContent = response.Content
                    .ReadAsStringAsync()
                    .WaitForResult();

                var data = JsonConvert.DeserializeObject<JObject>(responseContent);

                if (data["contracts"] is not JArray contracts)
                    return false;

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
                            Ask = ask
                        };
                    }
                    catch
                    {
                        _log.LogError("Can't update tezos tokens quotes");
                    }
                }

                _log.LogDebug("Update finished");

                return true;
            }
            catch (Exception e)
            {
                _log.LogError(e, e.Message);

                return false;
            }
        }
    }
}