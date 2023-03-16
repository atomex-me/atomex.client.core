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
        private string BaseUrl { get; } = "https://proxy.atomex.me/";

        public TezToolsQuotesProvider(ILogger? log = null)
        {
            Log = log;
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

        public override async Task UpdateAsync(CancellationToken cancellationToken = default)
        {
            Log?.LogDebug("Start updating TezTools quotes");
            bool isAvailable;

            try
            {
                isAvailable = await UpdateQuotesAsync(cancellationToken)
                    .ConfigureAwait(false);

                Log?.LogDebug("Update TezTools quotes finished");
            }
            catch (Exception e)
            {
                Log?.LogError(e, e.Message);

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
            using var response = await HttpHelper
                .GetAsync(
                    baseUri: BaseUrl,
                    relativeUri: "token/prices",
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

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

                    var bid = token["sellPrice"]!.Value<decimal>();
                    var ask = token["buyPrice"]!.Value<decimal>();

                    Quotes[symbol.ToLower()] = new Quote
                    {
                        Bid = bid,
                        Ask = ask
                    };
                }
                catch
                {
                    Log?.LogError("Can't update TezTools quotes");
                }
            }

            return true;
        }
    }
}