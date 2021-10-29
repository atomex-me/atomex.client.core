using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Serilog;
using Websocket.Client;

using Atomex.Core;
using Atomex.Common;
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
            public string StreamId { get; set; }
            [JsonProperty("data")]
            public T Data { get; set; }
        }

        private class OrderBookUpdates
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

        private class OrderBookStream
        {
            public MarketDataOrderBook OrderBook { get; set; }
            public List<Entry> Entries { get; set; }
            public State State { get; set; }
            public SemaphoreSlim Semaphore { get; set; }

            public OrderBookStream(string s)
            {
                OrderBook = new MarketDataOrderBook(s);
                Entries = new List<Entry>();
                State = State.Sync;
                Semaphore = new SemaphoreSlim(1, 1);
            }
        }

        private const string WsBaseUrl = "wss://stream.binance.com:9443/stream";
        private const string BaseUrl = "https://api.binance.com/api/v3/";

        private readonly Dictionary<string, OrderBookStream> _streams;
        private readonly string[] _symbols;
        private readonly BinanceOrderBookDepth _depth;
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

        public BinanceWebSocketOrderBooksProvider(
            BinanceOrderBookDepth depth,
            BinanceUpdateSpeed updateSpeed,
            params string[] symbols)
        {
            _depth = depth;
            _updateSpeed = updateSpeed;
            _symbols = symbols
                .Select(s => Symbols[s])
                .Distinct()
                .ToArray();
            _streams = _symbols
                .ToDictionary(
                    s => StreamBySymbol(s, (int)_depth, (int)_updateSpeed),
                    s => new OrderBookStream(s));
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

        private async Task UpdateSnapshotsAsync(string[] symbols, int depth, int updateSpeed)
        {
            foreach (var symbol in symbols)
                await UpdateSnapshotAsync(symbol, depth, updateSpeed)
                    .ConfigureAwait(false);
        }

        private async Task UpdateSnapshotAsync(string symbol, int depth, int updateSpeed)
        {
            var snapshot = await HttpHelper.GetAsync(
                baseUri: BaseUrl,
                requestUri: $"depth?symbol={symbol.ToUpper()}&limit={depth}",
                responseHandler: (response) =>
                {
                    if (!response.IsSuccessStatusCode)
                    {
                        Log.Error($"Invalid status code: {response.StatusCode}");
                        return null;
                    }

                    var responseContent = response.Content
                        .ReadAsStringAsync()
                        .WaitForResult();

                    return JsonConvert.DeserializeObject<OrderBookUpdates>(responseContent);
                });

            if (snapshot == null)
            {
                Log.Error($"Null snapshot recevided for {symbol}");
                return;
            }

            var streamId = StreamBySymbol(symbol, depth, updateSpeed);

            if (!_streams.TryGetValue(streamId, out var orderBookStream))
            {
                Log.Error($"Can't find stream {streamId}");
                return;
            }

            try
            {
                await orderBookStream.Semaphore
                    .WaitAsync()
                    .ConfigureAwait(false);

                var entries = GetEntries(snapshot);

                orderBookStream.OrderBook.ApplySnapshot(new Snapshot
                {
                    Entries = entries,
                    Symbol = symbol,
                    LastTransactionId = snapshot.LastUpdateId
                });

                foreach (var entry in orderBookStream.Entries)
                    if (entry.TransactionId > snapshot.LastUpdateId)
                        orderBookStream.OrderBook.ApplyEntry(entry);

                orderBookStream.State = State.Ready;
            }
            finally
            {
                orderBookStream.Semaphore.Release();
            }
        }

        public static string StreamBySymbol(string symbol, int depth, int updateSpeed) =>
            $"{symbol}@depth{depth}@{updateSpeed}ms";

        private void OnConnectedEventHandler(object sender, EventArgs args)
        {
            IsAvailable = true;

            var streamIds = _streams
                .Keys
                .ToArray();

            SubscribeToStreams(streamIds);

            _ = UpdateSnapshotsAsync(_symbols, (int)_depth, (int)_updateSpeed);
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

            var streamId = StreamBySymbol(symbol, (int)_depth, (int)_updateSpeed);

            return _streams.TryGetValue(streamId, out var stream)
                ? stream.OrderBook
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
                            var streamMessage = message.ToObject<Payload<OrderBookUpdates>>();

                            if (!_streams.TryGetValue(streamMessage.StreamId, out var orderBookStream))
                            {
                                Log.Debug($"Unknown stream {streamMessage.StreamId}");
                                return;
                            }

                            var entries = GetEntries(streamMessage.Data);

                            try
                            {
                                orderBookStream.Semaphore.Wait();

                                if (orderBookStream.State == State.Ready)
                                {
                                    // apply updates
                                    foreach (var entry in entries)
                                        orderBookStream.OrderBook.ApplyEntry(entry);

                                    orderBookStream.OrderBook.AdjustDepth((int)_depth);

                                    OrderBookUpdated?.Invoke(this, new OrderBookEventArgs(orderBookStream.OrderBook));
                                }
                                else
                                {
                                    // buffer all entries
                                    orderBookStream.Entries.AddRange(entries);
                                }
                            }
                            finally
                            {
                                orderBookStream.Semaphore.Release();
                            }
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

        private List<Entry> GetEntries(OrderBookUpdates updates)
        {
            var bids = updates.Bids
                .Select(pl => new Entry
                {
                    Price = decimal.Parse(pl[0], CultureInfo.InvariantCulture),
                    QtyProfile = new List<decimal>
                    {
                        decimal.Parse(pl[1], CultureInfo.InvariantCulture)
                    },
                    Side = Side.Buy,
                    TransactionId = updates.LastUpdateId
                });

            var asks = updates.Asks
                .Select(pl => new Entry
                {
                    Price = decimal.Parse(pl[0], CultureInfo.InvariantCulture),
                    QtyProfile = new List<decimal> {
                        decimal.Parse(pl[1], CultureInfo.InvariantCulture)
                    },
                    Side = Side.Sell,
                    TransactionId = updates.LastUpdateId
                });

            return bids
                .Concat(asks)
                .ToList();
        }
    }
}