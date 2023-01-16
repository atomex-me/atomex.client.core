using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
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

namespace Atomex.MarketData.Binance
{
    public class BinanceWebSocketOrderBooksProvider : IOrderBookProvider
    {
        private const int SnapshotLimit = 1000;

        private enum State
        {
            Sync,
            Ready
        }

        private class OrderBookStream
        {
            public OrderBook OrderBook { get; set; }
            public List<BinanceOrderBookUpdates> Updates { get; set; }
            public State State { get; set; }
            public SemaphoreSlim Semaphore { get; set; }

            public OrderBookStream(string s)
            {
                OrderBook = new OrderBook(s);
                Updates = new List<BinanceOrderBookUpdates>();
                State = State.Sync;
                Semaphore = new SemaphoreSlim(1, 1);
            }
        }

        private const string WsBaseUrl = "wss://stream.binance.com:9443/stream";
        private const string BaseUrl = "https://api.binance.com/api/v3/";

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

        private readonly Dictionary<string, OrderBookStream> _streams;
        private readonly string[] _symbols;
        private readonly BinanceUpdateSpeed _updateSpeed;
        private readonly ILogger _log;
        private WebSocketClient _ws;

        public BinanceWebSocketOrderBooksProvider(
            BinanceUpdateSpeed updateSpeed,
            ILogger log = null,
            params string[] symbols)
        {
            _log = log;
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

        public void Start() => StartAsync().WaitForResult();

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

        private async Task UpdateSnapshotsAsync(string[] symbols, int updateSpeed)
        {
            foreach (var symbol in symbols)
                await UpdateSnapshotAsync(symbol, updateSpeed)
                    .ConfigureAwait(false);
        }

        private async Task UpdateSnapshotAsync(
            string symbol,
            int updateSpeed,
            CancellationToken cancellationToken = default)
        {
            using var response = await HttpHelper
                .GetAsync(
                    baseUri: BaseUrl,
                    relativeUri: $"depth?symbol={symbol.ToUpper()}&limit={SnapshotLimit}",
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                _log?.LogError($"Invalid status code: {response.StatusCode}");
                return;
            }

            var responseContent = response
                .Content
                .ReadAsStringAsync()
                .WaitForResult();

            var snapshot = JsonSerializer.Deserialize<BinancePartialOrderBookUpdates>(responseContent);

            if (snapshot == null)
            {
                _log?.LogError($"Null snapshot recevided for {symbol}");
                return;
            }

            var streamId = StreamBySymbol(symbol, updateSpeed);

            if (!_streams.TryGetValue(streamId, out var orderBookStream))
            {
                _log?.LogError($"Can't find stream {streamId}");
                return;
            }

            try
            {
                await orderBookStream
                    .Semaphore
                    .WaitAsync(cancellationToken)
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
                        _log?.LogWarning("Something wrong!");
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

        public OrderBook GetOrderBook(string currency, string quoteCurrency)
        {
            var symbol = BinanceCommon.Symbols.Keys.Contains($"{currency}/{quoteCurrency}")
                ? BinanceCommon.Symbols[$"{currency}/{quoteCurrency}"]
                : null;

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
            var streamMessage = JsonSerializer.Deserialize<BinancePayload<BinanceOrderBookUpdates>>(jsonMessage);

            if (!_streams.TryGetValue(streamMessage.StreamId, out var orderBookStream))
            {
                _log?.LogDebug($"Unknown stream {streamMessage.StreamId}");
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
    }
}