using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.WebSockets;
using System.Threading.Tasks;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Serilog;
using Websocket.Client;

using Atomex.Common;
using Atomex.MarketData.Abstract;
using WebSocketClient = Atomex.Common.WebSocketClient;

namespace Atomex.MarketData.Binance
{
    public class BinanceWebSocketPartialOrderBooksProvider : ICurrencyOrderBookProvider
    {
        private const string WsBaseUrl = "wss://stream.binance.com:9443/stream";

        private readonly Dictionary<string, MarketDataOrderBook> _orderBooks;
        private readonly string[] _symbols;
        private readonly BinancePartialOrderBookDepth _depth;
        private readonly BinanceUpdateSpeed _updateSpeed;
        private WebSocketClient _ws;

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

        public BinanceWebSocketPartialOrderBooksProvider(
            BinancePartialOrderBookDepth depth,
            BinanceUpdateSpeed updateSpeed,
            params string[] symbols)
        {
            _depth = depth;
            _updateSpeed = updateSpeed;
            _symbols = symbols
                .Select(s => BinanceCommon.Symbols[s])
                .Distinct()
                .ToArray();
            _orderBooks = _symbols
                .ToDictionary(
                    s => StreamBySymbol(s, (int)_depth, (int)_updateSpeed),
                    s => new MarketDataOrderBook(s));
        }

        public void Start()
        {
            StartAsync().WaitForResult();
        }

        public Task StartAsync()
        {
            _ws = new WebSocketClient(WsBaseUrl);

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
                method = "SUBSCRIBE",
                @params = streams,
                id = 1
            };

            var requestJson = JsonConvert.SerializeObject(request);

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

        public MarketDataOrderBook GetOrderBook(string currency, string quoteCurrency)
        {
            var symbol = BinanceCommon.Symbols.Keys.Contains($"{currency}/{quoteCurrency}") ?
                BinanceCommon.Symbols[$"{currency}/{quoteCurrency}"] :
                null;

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
                        var message = JsonConvert.DeserializeObject<JObject>(msg.Text);

                        if (message.ContainsKey("stream"))
                        {
                            var streamMessage = message.ToObject<BinancePayload<BinancePartialOrderBookUpdates>>();

                            if (!_orderBooks.TryGetValue(streamMessage.StreamId, out var orderBook))
                            {
                                Log.Debug($"Unknown stream {streamMessage.StreamId}");
                                return;
                            }

                            var entries = streamMessage.Data.GetEntries();

                            orderBook.Clear();

                            // apply updates
                            foreach (var entry in entries)
                                orderBook.ApplyEntry(entry);

                            OrderBookUpdated?.Invoke(this, new OrderBookEventArgs(orderBook));
                        }
                        else if (message.ContainsKey("result"))
                        {
                            Log.Debug(msg.Text);
                        }
                        else if (message.ContainsKey("code"))
                        {
                            Log.Debug(msg.Text);
                        }
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
    }
}