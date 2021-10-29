using System.Collections.Generic;
using System.Globalization;
using System.Linq;

using Newtonsoft.Json;

using Atomex.Core;

namespace Atomex.MarketData.Binance
{
    public class BinancePartialOrderBookUpdates
    {
        [JsonProperty("lastUpdateId")]
        public long LastUpdateId { get; set; }
        [JsonProperty("bids")]
        public List<List<string>> Bids { get; set; }
        [JsonProperty("asks")]
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