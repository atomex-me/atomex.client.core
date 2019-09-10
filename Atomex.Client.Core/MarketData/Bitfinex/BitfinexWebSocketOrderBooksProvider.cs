using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Atomex.Core;
using Atomex.Core.Entities;
using Atomex.MarketData.Abstract;
using Atomex.Wallet.Abstract;
using Newtonsoft.Json.Linq;
using Serilog;

namespace Atomex.MarketData.Bitfinex
{
    public class BitfinexWebSocketOrderBooksProvider : ICurrencyOrderBookProvider
    {
        private const int MaxReceiveBufferSize = 32768;
        private const int DefaultReceiveBufferSize = 4096;
        private const string BaseUrl = "wss://api-pub.bitfinex.com/ws/2";

        private readonly Dictionary<int, string> _channels;
        private readonly Dictionary<string, MarketDataOrderBook> _orderbooks;
        private ClientWebSocket _ws;
        private Task _wsTask;
        private CancellationTokenSource _cts;

        public event EventHandler OrderBookUpdated;
        public event EventHandler AvailabilityChanged;

        public DateTime LastUpdateTime { get; private set; }
        public DateTime LastSuccessUpdateTime { get; private set; }
        public int ReceiveBufferSize { get; set; } = DefaultReceiveBufferSize;
        public bool IsRunning => _wsTask != null &&
                                 !_wsTask.IsCompleted &&
                                 !_wsTask.IsCanceled &&
                                 !_wsTask.IsFaulted;

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

        public BitfinexWebSocketOrderBooksProvider(params Symbol[] symbols)
        {
            _channels = new Dictionary<int, string>();
            _orderbooks = symbols.ToDictionary(s => s.Name.Replace("/", ""), s => new MarketDataOrderBook(s));
        }

        public BitfinexWebSocketOrderBooksProvider(
            IAccount account,
            IEnumerable<Currency> currencies,
            string baseCurrency)
        {
            _channels = new Dictionary<int, string>();
            _orderbooks = currencies.ToDictionary(currency => $"{currency.Name}{baseCurrency}", currency => new MarketDataOrderBook(account.Symbols.GetByName($"{currency.Name}/{baseCurrency}")));
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
                    
                    await _ws.ConnectAsync(new Uri(BaseUrl), _cts.Token);

                    OnConnected();

                    while (_ws.State == WebSocketState.Open && !_cts.IsCancellationRequested)
                    {
                        var result = await _ws.ReceiveAsync(
                            buffer: new ArraySegment<byte>(buffer, bytesReceived, buffer.Length - bytesReceived),
                            cancellationToken: _cts.Token);

                        if (result.EndOfMessage)
                        {
                            bytesReceived += result.Count;

                            OnMessage(result, new ArraySegment<byte>(buffer, 0, bytesReceived));

                            bytesReceived = 0;
                        }
                        else if (buffer.Length == bytesReceived)
                        {
                            if (buffer.Length >= MaxReceiveBufferSize)
                            {
                                Log.Error("Message too big!");
                                await _ws.CloseAsync(WebSocketCloseStatus.MessageTooBig, "Message too big", _cts.Token);
                                break;
                            }

                            bytesReceived += result.Count;

                            var extendedBuffer = new byte[buffer.Length * 2];
                            Buffer.BlockCopy(buffer, 0, extendedBuffer, 0, bytesReceived);

                            buffer = extendedBuffer;   
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                Log.Debug("WebSocket task canceled");
            }
            catch (Exception e)
            {
                Log.Error(e, "WebSocket connect async error");
            }

            OnDisconnected();
        }

        public MarketDataOrderBook GetOrderBook(string currency, string baseCurrency)
        {
            return _orderbooks.TryGetValue($"{currency}{baseCurrency}", out var orderbook) ? orderbook : null;
        }

        private void OnConnected()
        {
            IsAvailable = true;
            SubscribeToTickers();
        }

        private void OnDisconnected()
        {
            IsAvailable = false;
        }

        private void OnMessage(WebSocketReceiveResult result, ArraySegment<byte> buffer)
        {
            try
            {
                if (result.MessageType == WebSocketMessageType.Text)
                {
                    var responseText = Encoding.UTF8.GetString(buffer.Array, buffer.Offset, buffer.Count);

                    var responseJson = JToken.Parse(responseText);

                    if (responseJson is JObject responseEvent)
                    {
                        HandleEvent(responseEvent);
                    }
                    else if (responseJson is JArray responseData)
                    {
                        HandleData(responseData);
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

                    if (items[0] is JArray)
                    {
                        foreach (var item in items) //it's a snapshot
                        {
                            try
                            {
                                var entry = new Entry
                                {
                                    Side = item[2].Value<decimal>() > 0 ? Side.Buy : Side.Sell,
                                    Price = item[0].Value<decimal>()
                                };

                                entry.QtyProfile.Add(item[1].Value<int>() > 0 ? Math.Abs(item[2].Value<decimal>()) : 0);
                                _orderbooks[symbol].ApplyEntry(entry);
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine(ex.ToString());
                            }
                        }
                    }
                    else
                    {
                        var entry = new Entry
                        {
                            Side = items[2].Value<decimal>() > 0 ? Side.Buy : Side.Sell,
                            Price = items[0].Value<decimal>()
                        };
                        entry.QtyProfile.Add(items[1].Value<int>() > 0 ? Math.Abs(items[2].Value<decimal>()) : 0);
                        _orderbooks[symbol].ApplyEntry(entry);
                    }

                    LastUpdateTime = timeStamp;
                    LastSuccessUpdateTime = timeStamp;

                    OrderBookUpdated?.Invoke(this, EventArgs.Empty);
                }
            }
            else
            {
                Log.Warning($"Unknown channel id {chanId}");
            }
        }

        private async void SubscribeToTickers()
        {
            try
            {
                foreach (var symbol in _orderbooks.Keys)
                {
                    var message =
                        $"{{ \"event\": \"subscribe\", \"channel\": \"book\", \"pair\":\"{symbol}\", \"prec\": \"P0\", \"freq\": \"F0\", \"len\": \"25\"}}";

                    var messageBytes = Encoding.UTF8.GetBytes(message);

                    await _ws.SendAsync(
                        buffer: new ArraySegment<byte>(messageBytes),
                        messageType: WebSocketMessageType.Text,
                        endOfMessage: true,
                        cancellationToken: CancellationToken.None);
                }
            }
            catch (Exception e)
            {
                Log.Error(e, "Subscribe to tickers error");
            }
        }
    }
}