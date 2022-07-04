using System.Text.Json.Serialization;

namespace Atomex.MarketData.Binance
{
    public class BinancePayload<T>
    {
        [JsonPropertyName("stream")]
        public string StreamId { get; set; }
        [JsonPropertyName("data")]
        public T Data { get; set; }
    }
}