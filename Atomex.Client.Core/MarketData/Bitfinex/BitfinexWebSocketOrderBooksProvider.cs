using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Atomex.Core;
using Atomex.MarketData.Abstract;
using Newtonsoft.Json.Linq;
using Serilog;

namespace Atomex.MarketData.Bitfinex
{
    public class BitfinexWebSocketOrderBooksProvider : ICurrencyOrderBookProvider
    {
        private readonly Dictionary<string, string> Symbols = new Dictionary<string, string>()
        {
            { "ETHBTC", "ETHBTC" },
            { "LTCBTC", "LTCBTC" },
            { "XTZBTC", "XTZBTC" },
            { "XTZETH", "XTZETH" },
            { "BTCUSDT", "BTCUST" },
            { "ETHUSDT", "ETHUST" },
            { "LTCUSDT", "LTCUST" },
            { "XTZUSDT", "XTZUST" },
            { "ETHNYX", "ETHBTC" },
            { "XTZNYX", "XTZBTC" },
            { "FA2ETH", "XTZETH" },
            { "FA2BTC", "XTZBTC" },
            { "ETHTZBTC", "ETHBTC" },
            { "XTZTZBTC", "XTZBTC" },
            { "TZBTCUSDT", "BTCUST" },

            { "ETHTBTC", "ETHBTC" },
            { "XTZTBTC", "XTZBTC" },
            { "TBTCUSDT", "BTCUST" },

            { "ETHWBTC", "ETHBTC" },
            { "XTZWBTC", "XTZBTC" },
            { "WBTCUSDT", "BTCUST" },
        };

        private const int MaxReceiveBufferSize = 32768;
        private const int DefaultReceiveBufferSize = 32768;
        private const string BaseUrl = "wss://api-pub.bitfinex.com/ws/2";

        private readonly Dictionary<int, string> _channels;
        private readonly Dictionary<string, MarketDataOrderBook> _orderbooks;
        private ClientWebSocket _ws;
        private Task _wsTask;
        private Task _pingTask;
        private CancellationTokenSource _cts;

        public event EventHandler<OrderBookEventArgs> OrderBookUpdated;
        public event EventHandler AvailabilityChanged;

        public DateTime LastUpdateTime { get; private set; }
        public int ReceiveBufferSize { get; set; } = DefaultReceiveBufferSize;
        public int BookDepth { get; set; } = 100;
        public bool IsRunning => _wsTask != null &&
                                 !_wsTask.IsCompleted &&
                                 !_wsTask.IsCanceled &&
                                 !_wsTask.IsFaulted;
        public bool IsRestart { get; private set; }

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

        public BitfinexWebSocketOrderBooksProvider(params string[] symbols)
        {
            _channels = new Dictionary<int, string>();

            _orderbooks = symbols
                .Select(s => Symbols[s.Replace("/", "")])
                .Distinct()
                .ToDictionary(s => s, s => new MarketDataOrderBook(s));
        }

        public void Start()
        {
            if (IsRunning)
            {
                Log.Warning("OrderBook provider already running");
                return;
            }

            _cts = new CancellationTokenSource();
            _wsTask = Task.Run(Run, _cts.Token);
        }

        public void Stop()
        {
            if (IsRunning)
            {
                _cts.Cancel();
            }
            else
            {
                Log.Warning("OrderBook provider task already finished");
            }
        }

        private async Task Run()
        {
            try
            {
                using (_ws = new ClientWebSocket())
                {
                    var bytesReceived = 0;
                    var buffer = new byte[ReceiveBufferSize];
                    
                    await _ws.ConnectAsync(new Uri(BaseUrl), _cts.Token)
                        .ConfigureAwait(false);

                    await OnConnectedAsync()
                        .ConfigureAwait(false);

                    while (_ws.State == WebSocketState.Open && !_cts.IsCancellationRequested)
                    {
                        var result = await _ws.ReceiveAsync(
                                buffer: new ArraySegment<byte>(buffer, bytesReceived, buffer.Length - bytesReceived),
                                cancellationToken: _cts.Token)
                            .ConfigureAwait(false);

                        if (result.EndOfMessage)
                        {
                            bytesReceived += result.Count;

                            await OnMessageAsync(result, new ArraySegment<byte>(buffer, 0, bytesReceived))
                                .ConfigureAwait(false);

                            bytesReceived = 0;
                        }
                        else if (buffer.Length == bytesReceived)
                        {
                            if (buffer.Length >= MaxReceiveBufferSize)
                            {
                                Log.Error("Message too big!");

                                await _ws.CloseAsync(WebSocketCloseStatus.MessageTooBig, "Message too big", _cts.Token)
                                    .ConfigureAwait(false);

                                break;
                            }

                            bytesReceived += result.Count;

                            var extendedBuffer = new byte[buffer.Length * 2];
                            Buffer.BlockCopy(buffer, 0, extendedBuffer, 0, bytesReceived);

                            buffer = extendedBuffer;   
                        }
                    }

                    if (_ws.State != WebSocketState.Closed && _ws.State != WebSocketState.Aborted)
                        await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Close", CancellationToken.None)
                            .ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                Log.Debug("WebSocket task canceled");
            }
            catch (Exception e)
            {
                Log.Error("WebSocket connect async error");
            }

            OnDisconnected();
        }

        public MarketDataOrderBook GetOrderBook(string currency, string quoteCurrency)
        {
            var symbol = Symbols.Keys.Contains($"{currency}{quoteCurrency}") ?
                Symbols[$"{currency}{quoteCurrency}"] :
                null;

            if (symbol == null)
                return null;

            return _orderbooks.TryGetValue(symbol, out var orderbook) ? orderbook : null;
        }

        private async Task OnConnectedAsync()
        {
            IsAvailable = true;

            await SubscribeToTickersAsync()
                .ConfigureAwait(false);

            _pingTask = Task.Run(async () =>
            {
                try
                {
                    while (IsRunning)
                    {
                        var message =
                            $"{{ \"event\": \"ping\", \"cid\": \"0\" }}";

                        var messageBytes = Encoding.UTF8.GetBytes(message);

                        await _ws.SendAsync(
                                buffer: new ArraySegment<byte>(messageBytes),
                                messageType: WebSocketMessageType.Text,
                                endOfMessage: true,
                                cancellationToken: CancellationToken.None)
                            .ConfigureAwait(false);

                        await Task.Delay(5000)
                            .ConfigureAwait(false);
                    }
                }
                catch (Exception e)
                {
                    Log.Error("Ping task error");

                    if (_ws.State == WebSocketState.Closed || _ws.State == WebSocketState.Aborted)
                        OnDisconnected();
                }
            });
        }

        private void OnDisconnected()
        {
            IsAvailable = false;

            if (IsRestart)
            {
                IsRestart = false;

                _wsTask.ContinueWith(t => {
                    _cts = new CancellationTokenSource();
                    return _wsTask = Task.Run(Run, _cts.Token);
                });
            }
        }

        private async Task OnMessageAsync(WebSocketReceiveResult result, ArraySegment<byte> buffer)
        {
            try
            {
                if (result.MessageType == WebSocketMessageType.Text)
                {
                    var responseText = Encoding.UTF8.GetString(buffer.Array, buffer.Offset, buffer.Count);

                    try
                    {
                        var responseJson = JToken.Parse(responseText);

                        if (responseJson is JObject responseEvent)
                        {
                            await HandleEventAsync(responseEvent)
                                .ConfigureAwait(false);
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
                Log.Error("Bitfinex response handle error");
            }
        }

        private async Task HandleEventAsync(JObject response)
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
                        IsRestart = true;
                        Stop();
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

                        await SubscribeToTickersAsync()
                            .ConfigureAwait(false);
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
                    var orderBook = _orderbooks[symbol];

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

        private async Task SubscribeToTickersAsync()
        {
            try
            {
                foreach (var symbol in _orderbooks.Keys)
                {
                    var message =
                        $"{{ \"event\": \"subscribe\", \"channel\": \"book\", \"pair\":\"{symbol}\", \"prec\": \"P0\", \"freq\": \"F0\", \"len\": \"{BookDepth}\"}}";

                    var messageBytes = Encoding.UTF8.GetBytes(message);

                    await _ws.SendAsync(
                            buffer: new ArraySegment<byte>(messageBytes),
                            messageType: WebSocketMessageType.Text,
                            endOfMessage: true,
                            cancellationToken: CancellationToken.None)
                        .ConfigureAwait(false);
                }
            }
            catch (Exception e)
            {
                Log.Error("Subscribe to tickers error");
            }
        }
    }
}