using System.Collections.Generic;

namespace Atomex.MarketData.Binance
{
    public static class BinanceCommon
    {
        public static Dictionary<string, string> Symbols = new()
        {
            { "ETH/BTC", "ethbtc" },
            { "LTC/BTC", "ltcbtc" },
            { "XTZ/BTC", "xtzbtc" },

            { "BTC/USDT", "btcusdt" },
            { "ETH/USDT", "ethusdt" },
            { "LTC/USDT", "ltcusdt" },
            { "XTZ/USDT", "xtzusdt" },

            { "ETH/TZBTC", "ethbtc" },
            { "XTZ/TZBTC", "xtzbtc" },
            { "TZBTC/USDT", "btcusdt" },

            { "ETH/TBTC", "ethbtc" },
            { "XTZ/TBTC", "xtzbtc" },
            { "TBTC/USDT", "btcusdt" },

            { "ETH/WBTC", "ethbtc" },
            { "XTZ/WBTC", "xtzbtc" },
            { "WBTC/USDT", "btcusdt" },

            { "BTC/KUSD", "btcusdt" },
            { "ETH/KUSD", "ethusdt" },
            { "LTC/KUSD", "ltcusdt" },
            { "XTZ/KUSD", "xtzusdt" },
            { "TZBTC/KUSD", "btcusdt" },

            { "BTC/USDT_XTZ", "btcusdt" },
            { "ETH/USDT_XTZ", "ethusdt" },
            { "LTC/USDT_XTZ", "ltcusdt" },
            { "XTZ/USDT_XTZ", "xtzusdt" },
            { "TZBTC/USDT_XTZ", "btcusdt" }
        };
    }
}