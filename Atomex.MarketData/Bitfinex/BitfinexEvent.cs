using System.Text.Json.Serialization;

namespace Atomex.MarketData.Bitfinex
{
    public class BitfinexEvent
    {
        [JsonPropertyName("event")]
        public string Event { get; set; }
        [JsonPropertyName("chanId")]
        public int ChanId { get; set; }
        [JsonPropertyName("symbol")]
        public string Symbol { get; set; }
        [JsonPropertyName("pair")]
        public string Pair { get; set; }
        [JsonPropertyName("code")]
        public int Code { get; set; }
        [JsonPropertyName("msg")]
        public string Message { get; set; }
    }
}