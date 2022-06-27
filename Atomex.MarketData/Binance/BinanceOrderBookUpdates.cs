using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Serialization;

using Atomex.Common;
using Atomex.MarketData.Entities;

namespace Atomex.MarketData.Binance
{
    public class BinanceOrderBookUpdates
    {
        [JsonPropertyName("e")]
        public string EventType { get; set; }
        [JsonPropertyName("E")]
        public long EventTyme { get; set; }
        [JsonPropertyName("s")]
        public string Symbols { get; set; }
        [JsonPropertyName("U")]
        public long FirstUpdateId { get; set; }
        [JsonPropertyName("u")]
        public long FinalUpdateId { get; set; }
        [JsonPropertyName("b")]
        public List<List<string>> Bids { get; set; }
        [JsonPropertyName("a")]
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