using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.WebSockets;
using System.Text.Json;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;
using Websocket.Client;

using Atomex.Common;
using Atomex.MarketData.Abstract;
using Atomex.MarketData.Common;
using Atomex.MarketData.Entities;
using WebSocketClient = Atomex.Common.WebSocketClient;
using System.Text;

namespace Atomex.MarketData.Binance
{
    public class BinanceWebSocketPartialOrderBooksProvider : IOrderBookProvider
    {
        private const string WsBaseUrl = "wss://stream.binance.com:9443/stream";

        public event EventHandler<OrderBookEventArgs> OrderBookUpdated;
        public event EventHandler AvailabilityChanged;

        public DateTime LastUpdateTime { get; private set; }
        public string Name => "Binance WebSockets";

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

        private readonly Dictionary<string, OrderBook> _orderBooks;
        private readonly string[] _symbols;
        private readonly BinancePartialOrderBookDepth _depth;
        private readonly BinanceUpdateSpeed _updateSpeed;
        private readonly ILogger _log;
        private WebSocketClient _ws;

        public BinanceWebSocketPartialOrderBooksProvider(
            BinancePartialOrderBookDepth depth,
            BinanceUpdateSpeed updateSpeed,
            ILogger log = null,
            params string[] symbols)
        {
            _log = log;
            _depth = depth;
            _updateSpeed = updateSpeed;
            _symbols = symbols
                .Select(s => BinanceCommon.Symbols[s])
                .Distinct()
                .ToArray();
            _orderBooks = _symbols
                .ToDictionary(
                    s => StreamBySymbol(s, (int)_depth, (int)_updateSpeed),
                    s => new OrderBook(s));
        }

        public void Start() =>StartAsync().WaitForResult();

        public Task StartAsync()
        {
            _ws = new WebSocketClient(WsBaseUrl);

            _ws.Connected += OnConnectedEventHandler;
            _ws.Disconnected += OnDisconnectedEventHandler;
            _ws.OnMessage += OnMessageEventHandler;

            return _ws.ConnectAsync();
        }

        public void Stop() => StopAsync().WaitForResult();

        public Task StopAsync() => _ws.CloseAsync();

        private void SubscribeToStreams(string[] streams)
        {
            var requestJson = JsonSerializer.Serialize(new
            {
                method = "SUBSCRIBE",
                @params = streams,
                id = 1
            });

            _ws.Send(requestJson);
        }

        public static string StreamBySymbol(string symbol, int depth, int updateSpeed) =>
            $"{symbol}@depth{depth}@{updateSpeed}ms";

        private void OnConnectedEventHandler(object sender, EventArgs args)
        {
            IsAvailable = true;

            var streamIds = _orderBooks
                .Keys
                .ToArray();

            SubscribeToStreams(streamIds);
        }

        private void OnDisconnectedEventHandler(object sender, EventArgs e)
        {
            IsAvailable = false;
        }

        public OrderBook GetOrderBook(string currency, string quoteCurrency)
        {
            var symbol = BinanceCommon.Symbols.Keys.Contains($"{currency}/{quoteCurrency}")
                ? BinanceCommon.Symbols[$"{currency}/{quoteCurrency}"]
                : null;

            if (symbol == null)
                return null;

            var streamId = StreamBySymbol(symbol, (int)_depth, (int)_updateSpeed);

            return _orderBooks.TryGetValue(streamId, out var orderBook)
                ? orderBook
                : null;
        }

        private void OnMessageEventHandler(object sender, ResponseMessage msg)
        {
            try
            {
                if (msg.MessageType == WebSocketMessageType.Text)
                {
                    try
                    {
                        var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(msg.Text));

                        while (reader.Read())
                        {
                            if (reader.TokenType == JsonTokenType.PropertyName)
                            {
                                var propertyName = reader.GetString();

                                if (propertyName == "stream")
                                {
                                    ParseUpdates(msg.Text);
                                    break;
                                }
                                else if (propertyName == "result" || propertyName == "code")
                                {
                                    _log?.LogDebug(msg.Text);
                                    break;
                                }
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        _log?.LogError(e, "Binance text message handle error");
                    }
                }
            }
            catch (Exception e)
            {
                _log?.LogError(e, "Binance response handle error");
            }
        }

        private void ParseUpdates(string jsonMessage)
        {
            var streamMessage = JsonSerializer.Deserialize<BinancePayload<BinancePartialOrderBookUpdates>>(jsonMessage);

            if (!_orderBooks.TryGetValue(streamMessage.StreamId, out var orderBook))
            {
                _log?.LogDebug($"Unknown stream {streamMessage.StreamId}");
                return;
            }

            var entries = streamMessage.Data.GetEntries();

            orderBook.Clear();

            // apply updates
            foreach (var entry in entries)
                orderBook.ApplyEntry(entry);

            OrderBookUpdated?.Invoke(this, new OrderBookEventArgs(orderBook));
        }
    }
}