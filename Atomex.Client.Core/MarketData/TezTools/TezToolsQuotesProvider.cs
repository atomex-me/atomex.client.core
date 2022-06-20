using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Serilog;

using Atomex.Common;
using Atomex.MarketData.Abstract;

namespace Atomex.MarketData.TezTools
{
    public class TezToolsQuotesProvider : QuotesProvider
    {
        public const string Usd = "USD";

        private string BaseUrl { get; } = "https://test.atomex.me/";

        public TezToolsQuotesProvider()
        {
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
                var symbols = string.Join(",", Quotes
                    .Where(q => !q.Value.IsToken)
                    .Select(q => q.Key));

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
                            Ask = ask,
                            IsToken = true
                        };
                    }
                    catch
                    {
                        Log.Error("Can't update tezos tokens quotes");
                    }
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