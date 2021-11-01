using System.Collections.Generic;
using System.Globalization;
using System.Linq;

using Newtonsoft.Json;

using Atomex.Core;

namespace Atomex.MarketData.Binance
{
    public class BinanceOrderBookUpdates
    {
        [JsonProperty("e")]
        public string EventType { get; set; }
        [JsonProperty("E")]
        public long EventTyme { get; set; }
        [JsonProperty("s")]
        public string Symbols { get; set; }
        [JsonProperty("U")]
        public long FirstUpdateId { get; set; }
        [JsonProperty("u")]
        public long FinalUpdateId { get; set; }
        [JsonProperty("b")]
        public List<List<string>> Bids { get; set; }
        [JsonProperty("a")]
        public List<List<string>> Asks { get; set; }

        public List<Entry> GetEntries()
        {
            var bids = Bids
                .Select(pl => new Entry
                {
                    Price = decimal.Parse(pl[0], CultureInfo.InvariantCulture),
                    QtyProfile = new List<decimal>
                    {
                        decimal.Parse(pl[1], CultureInfo.InvariantCulture)
                    },
                    Side = Side.Buy,
                    TransactionId = FinalUpdateId
                });

            var asks = Asks
                .Select(pl => new Entry
                {
                    Price = decimal.Parse(pl[0], CultureInfo.InvariantCulture),
                    QtyProfile = new List<decimal>
                    {
                        decimal.Parse(pl[1], CultureInfo.InvariantCulture)
                    },
                    Side = Side.Sell,
                    TransactionId = FinalUpdateId
                });

            return bids
                .Concat(asks)
                .ToList();
        }
    }
}