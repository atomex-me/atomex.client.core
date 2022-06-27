using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Serialization;

using Atomex.Common;
using Atomex.MarketData.Entities;

namespace Atomex.MarketData.Binance
{
    public class BinancePartialOrderBookUpdates
    {
        [JsonPropertyName("lastUpdateId")]
        public long LastUpdateId { get; set; }
        [JsonPropertyName("bids")]
        public List<List<string>> Bids { get; set; }
        [JsonPropertyName("asks")]
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
                    TransactionId = LastUpdateId
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
                    TransactionId = LastUpdateId
                });

            return bids
                .Concat(asks)
                .ToList();
        }
    }
}