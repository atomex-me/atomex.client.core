using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Serilog;
using Websocket.Client;

using Atomex.Common;
using Atomex.MarketData.Abstract;
using WebSocketClient = Atomex.Web.WebSocketClient;

namespace Atomex.MarketData.Binance
{
    public class BinanceWebSocketOrderBooksProvider : ICurrencyOrderBookProvider
    {
        private const int SnapshotLimit = 1000;

        private enum State
        {
            Sync,
            Ready
        }

        private class OrderBookStream
        {
            public MarketDataOrderBook OrderBook { get; set; }
            public List<BinanceOrderBookUpdates> Updates { get; set; }
            public State State { get; set; }
            public SemaphoreSlim Semaphore { get; set; }

            public OrderBookStream(string s)
            {
                OrderBook = new MarketDataOrderBook(s);
                Updates = new List<BinanceOrderBookUpdates>();
                State = State.Sync;
                Semaphore = new SemaphoreSlim(1, 1);
            }
        }

        private const string WsBaseUrl = "wss://stream.binance.com:9443/stream";
        private const string BaseUrl = "https://api.binance.com/api/v3/";

        private readonly Dictionary<string, OrderBookStream> _streams;
        private readonly string[] _symbols;
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
            BinanceUpdateSpeed updateSpeed,
            params string[] symbols)
        {
            _updateSpeed = updateSpeed;
            _symbols = symbols
                .Select(s => BinanceCommon.Symbols[s])
                .Distinct()
                .ToArray();
            _streams = _symbols
                .ToDictionary(
                    s => StreamBySymbol(s, (int)_updateSpeed),
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

        private async Task UpdateSnapshotsAsync(string[] symbols, int updateSpeed)
        {
            foreach (var symbol in symbols)
                await UpdateSnapshotAsync(symbol, updateSpeed)
                    .ConfigureAwait(false);
        }

        private async Task UpdateSnapshotAsync(string symbol, int updateSpeed)
        {
            using var response = await HttpHelper.GetAsync(
                baseUri: BaseUrl,
                relativeUri: $"depth?symbol={symbol.ToUpper()}&limit={SnapshotLimit}");

            if (!response.IsSuccessStatusCode)
            {
                Log.Error($"Invalid status code: {response.StatusCode}");
                return;
            }

            var responseContent = await response.Content
                .ReadAsStringAsync()
                .ConfigureAwait(false);

            var snapshot = JsonConvert.DeserializeObject<BinancePartialOrderBookUpdates>(responseContent);

            if (snapshot == null)
            {
                Log.Error($"Null snapshot recevided for {symbol}");
                return;
            }

            var streamId = StreamBySymbol(symbol, updateSpeed);

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

                var snapshotEntries = snapshot.GetEntries();

                orderBookStream.OrderBook.ApplySnapshot(new Snapshot
                {
                    Entries = snapshotEntries,
                    Symbol = symbol,
                    LastTransactionId = snapshot.LastUpdateId
                });

                foreach (var update in orderBookStream.Updates)
                {
                    if (update.FinalUpdateId <= snapshot.LastUpdateId)
                    {
                        continue;
                    }
                    else if (update.FirstUpdateId <= snapshot.LastUpdateId + 1 &&
                             update.FinalUpdateId >= snapshot.LastUpdateId + 1)
                    {
                        var entries = update.GetEntries();

                        foreach (var entry in entries)
                            orderBookStream.OrderBook.ApplyEntry(entry);
                    }
                    else
                    {
                        Log.Warning("Something wrong!");
                    }
                }

                orderBookStream.Updates.Clear();
                orderBookStream.State = State.Ready;
            }
            finally
            {
                orderBookStream.Semaphore.Release();
            }
        }

        public static string StreamBySymbol(string symbol, int updateSpeed) =>
            $"{symbol}@depth@{updateSpeed}ms";

        private void OnConnectedEventHandler(object sender, EventArgs args)
        {
            IsAvailable = true;

            var streamIds = _streams
                .Keys
                .ToArray();

            SubscribeToStreams(streamIds);

            _ = UpdateSnapshotsAsync(_symbols, (int)_updateSpeed);
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

            var streamId = StreamBySymbol(symbol, (int)_updateSpeed);

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
                            var streamMessage = message.ToObject<BinancePayload<BinanceOrderBookUpdates>>();

                            if (!_streams.TryGetValue(streamMessage.StreamId, out var orderBookStream))
                            {
                                Log.Debug($"Unknown stream {streamMessage.StreamId}");
                                return;
                            }

                            try
                            {
                                orderBookStream.Semaphore.Wait();

                                if (orderBookStream.State == State.Ready) // apply updates
                                {
                                    var entries = streamMessage.Data.GetEntries();

                                    foreach (var entry in entries)
                                        orderBookStream.OrderBook.ApplyEntry(entry);

                                    OrderBookUpdated?.Invoke(this, new OrderBookEventArgs(orderBookStream.OrderBook));
                                }
                                else // otherwise save update to buffer
                                {
                                    orderBookStream.Updates.Add(streamMessage.Data);
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
    }
}