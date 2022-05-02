using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.WebSockets;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;
using Websocket.Client;

using Atomex.Common;
using Atomex.MarketData.Abstract;
using Atomex.MarketData.Common;
using Atomex.MarketData.Entities;
using WebSocketClient = Atomex.Common.WebSocketClient;

namespace Atomex.MarketData.Bitfinex
{
    public class BitfinexWebSocketOrderBooksProvider : IOrderBookProvider
    {
        private readonly Dictionary<string, string> Symbols = new()
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

        public event EventHandler<OrderBookEventArgs> OrderBookUpdated;
        public event EventHandler AvailabilityChanged;

        public DateTime LastUpdateTime { get; private set; }
        public string Name => "Bitfinex WebSockets";

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

        private readonly Dictionary<int, string> _channels;
        private readonly Dictionary<string, OrderBook> _orderBooks;
        private readonly TimeSpan _pingInterval = TimeSpan.FromSeconds(5);
        private readonly int _bookDepth = 25;
        private readonly ILogger _log;
        private WebSocketClient _ws;
        private CancellationTokenSource _pingCts;

        public BitfinexWebSocketOrderBooksProvider(
            ILogger log = null,
            params string[] symbols)
        {
            _log = log;
            _channels = new Dictionary<int, string>();

            _orderBooks = symbols
                .Select(s => Symbols[s])
                .Distinct()
                .ToDictionary(s => s, s => new OrderBook(s));
        }

        public void Start() => StartAsync().WaitForResult();

        public Task StartAsync()
        {
            _ws = new WebSocketClient(BaseUrl);

            _ws.Connected += OnConnectedEventHandler;
            _ws.Disconnected += OnDisconnectedEventHandler;
            _ws.OnMessage += OnMessageEventHandler;

            return _ws.ConnectAsync();
        }

        public void Stop() => StopAsync().WaitForResult();

        public Task StopAsync() => _ws.CloseAsync();

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

        public OrderBook GetOrderBook(string currency, string quoteCurrency)
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
                    if (msg.Text.StartsWith("{"))
                    {
                        HandleEvent(msg.Text);
                    }
                    else if (msg.Text.StartsWith("["))
                    {
                        HandleData(msg.Text);
                    }
                }
            }
            catch (Exception e)
            {
                _log.LogError(e, "Bitfinex response handle error");
            }
        }

        private void HandleEvent(string msg)
        {
            if (!TryParseEvent(msg, out var eventMsg))
            {
                _log.LogWarning("Unknown response type");
                return;
            }

            if (eventMsg.Event == "subscribed")
            {
                _channels[eventMsg.ChanId] = eventMsg.Pair;
            }
            else if (eventMsg.Event == "info")
            {
                if (eventMsg.Code != 0)
                {
                    if (eventMsg.Code == 20051)
                    {
                        // please reconnect
                        Stop();
                        Start();
                    }
                    else if (eventMsg.Code == 20060)
                    {
                        // technical service started
                        IsAvailable = false;
                    }
                    else if (eventMsg.Code == 20061)
                    {
                        // technical service stopped
                        IsAvailable = true;
                        SubscribeToTickers();
                    }
                }
            }
            else if (eventMsg.Event == "pong")
            {
                // nothing todo
            }
            else if (eventMsg.Event == "error")
            {
                _log.LogError($"Bitfinex error with code: {eventMsg.Code} and message \"{eventMsg.Message}\"");
            }
        }

        private void HandleData(string msg)
        {
            using var jsonDocument = JsonDocument.Parse(msg);

            var chanId = jsonDocument.RootElement[0].GetInt32();

            if (_channels.TryGetValue(chanId, out var symbol))
            {
                if (jsonDocument.RootElement[1].ValueKind != JsonValueKind.Array)
                    return;

                var timeStamp = DateTime.Now;
                var orderBook = _orderBooks[symbol];

                if (jsonDocument.RootElement[1][0].ValueKind != JsonValueKind.Array)
                {
                    orderBook.Clear();

                    foreach (var item in jsonDocument.RootElement[1][0].EnumerateArray()) //it's a snapshot
                    {
                        try
                        {
                            var entry = new Entry
                            {
                                Side = item[2].GetDecimal() > 0 ? Side.Buy : Side.Sell,
                                Price = item[0].GetDecimal()
                            };

                            entry.QtyProfile.Add(item[1].GetInt32() > 0
                                ? Math.Abs(item[2].GetDecimal())
                                : 0);

                            orderBook.ApplyEntry(entry);
                        }
                        catch (Exception ex)
                        {
                            _log.LogError(ex, "Snapshot apply error");
                        }
                    }
                }
                else
                {
                    try
                    {
                        var entry = new Entry
                        {
                            Side = jsonDocument.RootElement[1][2].GetDecimal() > 0 ? Side.Buy : Side.Sell,
                            Price = jsonDocument.RootElement[1][0].GetDecimal()
                        };

                        entry.QtyProfile.Add(jsonDocument.RootElement[1][1].GetInt32() > 0
                            ? Math.Abs(jsonDocument.RootElement[1][2].GetDecimal())
                            : 0);

                        orderBook.ApplyEntry(entry);
                    }
                    catch (Exception ex)
                    {
                        _log.LogError(ex, "Orderbook update apply error");
                    }
                }

                LastUpdateTime = timeStamp;

                OrderBookUpdated?.Invoke(this, new OrderBookEventArgs(orderBook));
            }
            else
            {
                _log.LogWarning($"Unknown channel id {chanId}");
            }
        }

        private void SubscribeToTickers()
        {
            try
            {
                foreach (var symbol in _orderBooks.Keys)
                {
                    var message = $"{{" +
                        $"\"event\":\"subscribe\"," +
                        $"\"channel\":\"book\"," +
                        $"\"pair\":\"{symbol}\"," +
                        $"\"prec\":\"P0\"," +
                        $"\"freq\":\"F0\"," +
                        $"\"len\":\"{_bookDepth}\"" +
                        $"}}";

                    _ws.Send(message);
                }
            }
            catch (Exception e)
            {
                _log.LogError(e, "Subscribe to tickers error");
            }
        }

        private void StartPingLoop()
        {
            _pingCts = new CancellationTokenSource();

            _ = Task.Run(async () =>
            {
                try
                {
                    var pingMessage = $"{{\"event\":\"ping\",\"cid\":\"0\"}}";

                    while (!_pingCts.IsCancellationRequested)
                    {
                        _ws.Send(pingMessage);

                        await Task
                            .Delay(_pingInterval, _pingCts.Token)
                            .ConfigureAwait(false);
                    }
                }
                catch (TaskCanceledException)
                {
                    // nothing to do
                }
                catch (Exception e)
                {
                    _log.LogError(e, "Ping task error");
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

        private static bool TryParseEvent(string msg, out BitfinexEvent @event)
        {
            @event = null;

            try
            {
                @event = JsonSerializer.Deserialize<BitfinexEvent>(msg);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}