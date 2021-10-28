using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;

using Newtonsoft.Json.Linq;
using Serilog;
using Websocket.Client;

using Atomex.Common;
using Atomex.Core;
using Atomex.MarketData.Abstract;
using WebSocketClient = Atomex.Web.WebSocketClient;

namespace Atomex.MarketData.Bitfinex
{
    public class BitfinexWebSocketOrderBooksProvider : ICurrencyOrderBookProvider
    {
        private readonly Dictionary<string, string> Symbols = new ()
        {
            { "ETH/BTC", "ETHBTC" },
            { "LTC/BTC", "LTCBTC" },
            { "XTZ/BTC", "XTZBTC" },
            { "XTZ/ETH", "XTZETH" },

            { "BTC/USDT", "BTCUST" },
            { "ETH/USDT", "ETHUST" },
            { "LTC/USDT", "LTCUST" },
            { "XTZ/USDT", "XTZUST" },

            { "ETH/NYX", "ETHBTC" },
            { "XTZ/NYX", "XTZBTC" },

            { "FA2/ETH", "XTZETH" },
            { "FA2/BTC", "XTZBTC" },

            { "ETH/TZBTC", "ETHBTC" },
            { "XTZ/TZBTC", "XTZBTC" },
            { "TZBTC/USDT", "BTCUST" },

            { "ETH/TBTC", "ETHBTC" },
            { "XTZ/TBTC", "XTZBTC" },
            { "TBTC/USDT", "BTCUST" },

            { "ETH/WBTC", "ETHBTC" },
            { "XTZW/BTC", "XTZBTC" },
            { "WBTC/USDT", "BTCUST" },

            { "BTC/KUSD", "BTCUST" },
            { "ETH/KUSD", "ETHUST" },
            { "LTC/KUSD", "LTCUST" },
            { "XTZ/KUSD", "XTZUST" },
            { "TZBTC/KUSD", "BTCUST" },
        };

        private const string BaseUrl = "wss://api-pub.bitfinex.com/ws/2";

        private readonly Dictionary<int, string> _channels;
        private readonly Dictionary<string, MarketDataOrderBook> _orderBooks;
        private WebSocketClient _ws;

        private CancellationTokenSource _pingCts;
        private TimeSpan _pingInterval = TimeSpan.FromSeconds(5);

        public event EventHandler<OrderBookEventArgs> OrderBookUpdated;
        public event EventHandler AvailabilityChanged;

        public DateTime LastUpdateTime { get; private set; }
        public int BookDepth { get; set; } = 100;

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

        public string Name => "Bitfinex WebSockets";

        public BitfinexWebSocketOrderBooksProvider(params string[] symbols)
        {
            _channels = new Dictionary<int, string>();

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

        private void OnConnectedEventHandler(object sender, EventArgs args)
        {
            IsAvailable = true;

            SubscribeToTickers();

            StartPingLoop();
        }

        private void OnDisconnectedEventHandler(object sender, EventArgs e)
        {
            IsAvailable = false;

            StopPingLoop();
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
                        var responseJson = JToken.Parse(msg.Text);

                        if (responseJson is JObject responseEvent)
                        {
                            HandleEvent(responseEvent);
                        }
                        else if (responseJson is JArray responseData)
                        {
                            HandleData(responseData);
                        }
                    }
                    catch (Exception e)
                    {
                        Log.Error(e, "Bitfinex response handle error");
                    }
                }
            }
            catch (Exception e)
            {
                Log.Error(e, "Bitfinex response handle error");
            }
        }

        private void HandleEvent(JObject response)
        {
            if (!response.ContainsKey("event"))
            {
                Log.Warning("Unknown response type");
                return;
            }

            var @event = response["event"].Value<string>();

            if (@event == "subscribed")
            {
                _channels[response["chanId"].Value<int>()] = response["pair"].Value<string>();
            }
            else if (@event == "info")
            {
                if (response.ContainsKey("code"))
                {
                    var code = response["code"].Value<int>();

                    if (code == 20051)
                    {
                        // please reconnect
                        Stop();
                        Start();
                    }
                    else if (code == 20060)
                    {
                        // technical service started
                        IsAvailable = false;
                    }
                    else if (code == 20061)
                    {
                        // technical service stopped
                        IsAvailable = true;

                        SubscribeToTickers();
                    }
                }
            }
            else if (@event == "pong")
            {
                // nothing todo
            }
            else if (@event == "error")
            {
                Log.Error($"Bitfinex error with code: {response["code"].Value<int>()} and message \"{response["msg"].Value<string>()}\"");
            }
        }

        private void HandleData(JArray response)
        {
            var chanId = response[0].Value<int>();

            if (_channels.TryGetValue(chanId, out var symbol))
            {
                if (response[1] is JArray items)
                {
                    var timeStamp = DateTime.Now;
                    var orderBook = _orderBooks[symbol];

                    if (items[0] is JArray)
                    {
                        orderBook.Clear();

                        foreach (var item in items) //it's a snapshot
                        {
                            try
                            {
                                var entry = new Entry
                                {
                                    Side = item[2].Value<decimal>() > 0 ? Side.Buy : Side.Sell,
                                    Price = item[0].Value<decimal>()
                                };

                                entry.QtyProfile.Add(item[1].Value<int>() > 0
                                    ? Math.Abs(item[2].Value<decimal>())
                                    : 0);

                                orderBook.ApplyEntry(entry);
                            }
                            catch (Exception ex)
                            {
                                Log.Error(ex, "Snapshot apply error");
                            }
                        }
                    }
                    else
                    {
                        try
                        {
                            var entry = new Entry
                            {
                                Side = items[2].Value<decimal>() > 0 ? Side.Buy : Side.Sell,
                                Price = items[0].Value<decimal>()
                            };

                            entry.QtyProfile.Add(items[1].Value<int>() > 0
                                ? Math.Abs(items[2].Value<decimal>())
                                : 0);

                            orderBook.ApplyEntry(entry);
                        }
                        catch (Exception ex)
                        {
                            Log.Error(ex, "Orderbook update apply error");
                        }
                    }

                    LastUpdateTime = timeStamp;

                    OrderBookUpdated?.Invoke(this, new OrderBookEventArgs(orderBook));
                }
            }
            else
            {
                Log.Warning($"Unknown channel id {chanId}");
            }
        }

        private void SubscribeToTickers()
        {
            try
            {
                foreach (var symbol in _orderBooks.Keys)
                {
                    var message =
                        $"{{ \"event\": \"subscribe\", \"channel\": \"book\", \"pair\":\"{symbol}\", \"prec\": \"P0\", \"freq\": \"F0\", \"len\": \"{BookDepth}\"}}";

                    _ws.Send(message);
                }
            }
            catch (Exception e)
            {
                Log.Error(e, "Subscribe to tickers error");
            }
        }

        private void StartPingLoop()
        {
            _pingCts = new CancellationTokenSource();
            _ = Task.Run(async () =>
            {
                try
                {
                    var pingMessage = $"{{ \"event\": \"ping\", \"cid\": \"0\" }}";

                    while (!_pingCts.IsCancellationRequested)
                    {
                        _ws.Send(pingMessage);

                        await Task.Delay(_pingInterval, _pingCts.Token)
                            .ConfigureAwait(false);
                    }
                }
                catch (TaskCanceledException)
                {
                    // nothing to do
                }
                catch (Exception e)
                {
                    Log.Error(e, "Ping task error");
                }

            }, _pingCts.Token);
        }

        private void StopPingLoop()
        {
            try
            {
                _pingCts.Cancel();
            }
            finally
            {

            }
        }
    }
}