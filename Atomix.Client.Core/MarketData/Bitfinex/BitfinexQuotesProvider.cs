using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Atomix.Common;
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
        public const string LtcBtc = "tLTCBTC";
        public const string EthBtc = "tETHBTC";
        public const string XtzBtc = "tXTZBTC";

        private string BaseUrl { get; } = "https://api.bitfinex.com/v2/";

        public BitfinexQuotesProvider(params string[] symbols)
        {
            Quotes = symbols.ToDictionary(s => s, s => new Quote());
        }

        public BitfinexQuotesProvider(IEnumerable<Currency> currencies, string baseCurrency)
        {
            Quotes = currencies.ToDictionary(currency => $"t{currency.Name}{baseCurrency}", currency => new Quote());
        }

        public override Quote GetQuote(string currency, string baseCurrency)
        {
            return Quotes.TryGetValue($"t{currency}{baseCurrency}", out var rate) ? rate : null;
        }

        protected override async Task UpdateAsync(
            CancellationToken cancellationToken = default(CancellationToken))
        {
            Log.Debug("Start of update");

            bool isAvailable;

            try
            {
                var symbols = string.Join(",", Quotes.Keys);

                var request = $"tickers?symbols={symbols}";

                isAvailable = await HttpHelper.GetAsync(
                        baseUri: BaseUrl,
                        requestUri: request,
                        responseHandler: responseContent =>
                        {
                            var tickers = JsonConvert.DeserializeObject<JArray>(responseContent);

                            foreach (var tickerToken in tickers)
                            {
                                if (!(tickerToken is JArray ticker))
                                    continue;

                                var symbol = ticker[0].Value<string>();

                                var bid = ticker[1].Value<decimal>();
                                var ask = ticker[3].Value<decimal>();

                                Quotes[symbol] = new Quote()
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