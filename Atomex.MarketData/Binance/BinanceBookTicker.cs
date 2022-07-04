using System.Text.Json.Serialization;

namespace Atomex.MarketData.Binance
{
    public class BinanceBookTicker
    {
        [JsonPropertyName("bidPrice")]
        public decimal Bid { get; set; }
        [JsonPropertyName("askPrice")]
        public decimal Ask { get; set; }
        [JsonPropertyName("bidQty")]
        public decimal BidQty { get; set; }
        [JsonPropertyName("askQty")]
        public decimal AskQty { get; set; }
    }
}