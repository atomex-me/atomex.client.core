using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;

using Newtonsoft.Json;
using Serilog;
using Websocket.Client;

using Atomex.Common;
using Atomex.Core;
using Atomex.MarketData.Abstract;
using WebSocketClient = Atomex.Web.WebSocketClient;

namespace Atomex.MarketData.Binance
{
    public enum BinanceOrderBookDepth
    {
        Five = 5,
        Ten = 10,
        Twenty = 20
    }

    public enum BinanceUpdateSpeed
    {
        Ms100 = 100,
        Ms1000 = 1000
    }

    public class BinanceWebSocketOrderBooksProvider : ICurrencyOrderBookProvider
    {
        private class Payload<T>
        {
            [JsonProperty("stream")]
            public string Stream { get; set; }
            [JsonProperty("data")]
            public T Data { get; set; }
        }

        private class OrderBookUpdate
        {
            [JsonProperty("lastUpdateId")]
            public long LastUpdateId { get; set; }
            [JsonProperty("bids")]
            public List<List<string>> Bids { get; set; }
            [JsonProperty("asks")]
            public List<List<string>> Asks { get; set; }
        }

        private readonly Dictionary<string, string> Symbols = new()
        {
            { "ETH/BTC", "ethbtc" },
            { "LTC/BTC", "ltcbtc" },
            { "XTZ/BTC", "xtzbtc" },
            //{ "XTZ/ETH", "XTZETH" },

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
        };

        private enum State
        {
            Sync,
            Ready
        }

        private const string BaseUrl = "wss://stream.binance.com:9443/stream";

        private readonly Dictionary<string, MarketDataOrderBook> _orderBooks;
        private readonly BinanceOrderBookDepth _depth;
        private readonly BinanceUpdateSpeed _updateSpeed;
        private WebSocketClient _ws;
        private State _state;

        public event EventHandler<OrderBookEventArgs> OrderBookUpdated;
        public event EventHandler AvailabilityChanged;

        public DateTime LastUpdateTime { get; private set; }

        private bool _isAvailable;
        public bool IsAvailable
        {
            get => _isAvailable;
            private set
            {
                _isAvailable = value;
                AvailabilityChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public string Name => "Binance WebSockets";

        public BinanceWebSocketOrderBooksProvider(
            BinanceOrderBookDepth depth,
            BinanceUpdateSpeed updateSpeed,
            params string[] symbols)
        {
            _depth = depth;
            _updateSpeed = updateSpeed;

            _orderBooks = symbols
                .Select(s => Symbols[s])
                .Distinct()
                .ToDictionary(s => s, s => new MarketDataOrderBook(s));
        }

        public void Start()
        {
            StartAsync().WaitForResult();
        }

        public Task StartAsync()
        {
            _ws = new WebSocketClient(BaseUrl);

            _ws.Connected += OnConnectedEventHandler;
            _ws.Disconnected += OnDisconnectedEventHandler;
            _ws.OnMessage += OnMessageEventHandler;

            return _ws.ConnectAsync();
        }

        public void Stop()
        {
            StopAsync().WaitForResult();
        }

        public Task StopAsync()
        {
            return _ws.CloseAsync();
        }

        private void SubscribeToStreams(string[] streams)
        {
            var request = new
            {
                method  = "SUBSCRIBE",
                @params = streams,
                id      = 1
            };

            var requestJson = JsonConvert.SerializeObject(request);

            _ws.Send(requestJson);
        }

        private void OnConnectedEventHandler(object sender, EventArgs args)
        {
            _state = State.Sync;

            IsAvailable = true;

            var streams = _orderBooks.Keys
               .Select(s => $"{s}@depth{(int)_depth}@{(int)_updateSpeed}ms")
               .ToArray();

            SubscribeToStreams(streams);
        }

        private void OnDisconnectedEventHandler(object sender, EventArgs e)
        {
            IsAvailable = false;
        }

        public MarketDataOrderBook GetOrderBook(string currency, string quoteCurrency)
        {
            var symbol = Symbols.Keys.Contains($"{currency}/{quoteCurrency}") ?
                Symbols[$"{currency}/{quoteCurrency}"] :
                null;

            if (symbol == null)
                return null;

            return _orderBooks.TryGetValue(symbol, out var orderbook) ? orderbook : null;
        }

        private void OnMessageEventHandler(object sender, ResponseMessage msg)
        {
            try
            {
                if (msg.MessageType == WebSocketMessageType.Text)
                {
                    try
                    {
                        Console.WriteLine(msg.Text);
                        //var responseJson = JToken.Parse(msg.Text);

                        //if (responseJson is JObject responseEvent)
                        //{
                        //    HandleEvent(responseEvent);
                        //}
                        //else if (responseJson is JArray responseData)
                        //{
                        //    HandleData(responseData);
                        //}
                    }
                    catch (Exception e)
                    {
                        Log.Error(e, "Binance text message handle error");
                    }
                }
            }
            catch (Exception e)
            {
                Log.Error(e, "Binance response handle error");
            }
        }

        //private void HandleEvent(JObject response)
        //{
            //if (!response.ContainsKey("event"))
            //{
            //    Log.Warning("Unknown response type");
            //    return;
            //}

            //var @event = response["event"].Value<string>();

            //if (@event == "subscribed")
            //{
            //    _channels[response["chanId"].Value<int>()] = response["pair"].Value<string>();
            //}
            //else if (@event == "info")
            //{
            //    if (response.ContainsKey("code"))
            //    {
            //        var code = response["code"].Value<int>();

            //        if (code == 20051)
            //        {
            //            // please reconnect
            //            IsRestart = true;
            //            Stop();
            //        }
            //        else if (code == 20060)
            //        {
            //            // technical service started
            //            IsAvailable = false;
            //        }
            //        else if (code == 20061)
            //        {
            //            // technical service stopped
            //            IsAvailable = true;

            //            SubscribeToTickers();
            //        }
            //    }
            //}
            //else if (@event == "pong")
            //{
            //    // nothing todo
            //}
            //else if (@event == "error")
            //{
            //    Log.Error($"Bitfinex error with code: {response["code"].Value<int>()} and message \"{response["msg"].Value<string>()}\"");
            //}
        //}

        //private void HandleData(JArray response)
        //{
            //var chanId = response[0].Value<int>();

            //if (_channels.TryGetValue(chanId, out var symbol))
            //{
            //    if (response[1] is JArray items)
            //    {
            //        var timeStamp = DateTime.Now;
            //        var orderBook = _orderbooks[symbol];

            //        if (items[0] is JArray)
            //        {
            //            orderBook.Clear();

            //            foreach (var item in items) //it's a snapshot
            //            {
            //                try
            //                {
            //                    var entry = new Entry
            //                    {
            //                        Side = item[2].Value<decimal>() > 0 ? Side.Buy : Side.Sell,
            //                        Price = item[0].Value<decimal>()
            //                    };

            //                    entry.QtyProfile.Add(item[1].Value<int>() > 0
            //                        ? Math.Abs(item[2].Value<decimal>())
            //                        : 0);

            //                    orderBook.ApplyEntry(entry);
            //                }
            //                catch (Exception ex)
            //                {
            //                    Log.Error(ex, "Snapshot apply error");
            //                }
            //            }
            //        }
            //        else
            //        {
            //            try
            //            {
            //                var entry = new Entry
            //                {
            //                    Side = items[2].Value<decimal>() > 0 ? Side.Buy : Side.Sell,
            //                    Price = items[0].Value<decimal>()
            //                };

            //                entry.QtyProfile.Add(items[1].Value<int>() > 0
            //                    ? Math.Abs(items[2].Value<decimal>())
            //                    : 0);

            //                orderBook.ApplyEntry(entry);
            //            }
            //            catch (Exception ex)
            //            {
            //                Log.Error(ex, "Orderbook update apply error");
            //            }
            //        }

            //        LastUpdateTime = timeStamp;

            //        OrderBookUpdated?.Invoke(this, new OrderBookEventArgs(orderBook));
            //    }
            //}
            //else
            //{
            //    Log.Warning($"Unknown channel id {chanId}");
            //}
        //}
    }
}