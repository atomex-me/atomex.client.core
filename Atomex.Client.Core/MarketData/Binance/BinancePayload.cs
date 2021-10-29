
using Newtonsoft.Json;

namespace Atomex.MarketData.Binance
{
    public class BinancePayload<T>
    {
        [JsonProperty("stream")]
        public string StreamId { get; set; }
        [JsonProperty("data")]
        public T Data { get; set; }
    }
}