using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.WebSockets;
using System.Text.Json;

using Microsoft.Extensions.Logging;
using Websocket.Client;

using Atomex.MarketData.Abstract;
using Atomex.MarketData.Entities;
using WebsocketClient = Atomex.Common.WebSocketClient;

namespace Atomex.MarketData.Bitfinex
{
    public class BitfinexWebSocketQuotesProvider : IQuotesProvider
    {
        private const string BaseUrl = "wss://api-pub.bitfinex.com/ws/2";

        private readonly Dictionary<int, string> _channels;
        private readonly Dictionary<string, Quote> _quotes;
        private WebsocketClient _ws;
        private readonly ILogger _log;

        public event EventHandler QuotesUpdated;
        public event EventHandler AvailabilityChanged;

        public DateTime LastUpdateTime { get; private set; }
        public DateTime LastSuccessUpdateTime { get; private set; }

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

        public BitfinexWebSocketQuotesProvider(ILogger log = null, params string[] symbols)
        {
            _log = log;
            _channels = new Dictionary<int, string>();
            _quotes = symbols.ToDictionary(s => s, s => new Quote());
        }

        public BitfinexWebSocketQuotesProvider(
            IEnumerable<string> currencies,
            string baseCurrency,
            ILogger log = null)
        {
            _log = log;
            _channels = new Dictionary<int, string>();
            _quotes = currencies.ToDictionary(
                currency => $"t{currency}{baseCurrency}",
                currency => new Quote());
        }

        public void Start()
        {
            _ws = new WebsocketClient(BaseUrl);
            
            _ws.Connected += OnOpenEventHandler;
            _ws.Disconnected += OnCloseEventHandler;
            _ws.OnMessage += OnMessageEventHandler;

            _ws.ConnectAsync();
        }

        public void Stop()
        {
            _ws.CloseAsync();
        }

        public Quote GetQuote(string currency, string baseCurrency) =>
            _quotes.TryGetValue($"t{currency}{baseCurrency}", out var rate)
                ? rate
                : null;

        public Quote GetQuote(string symbol) =>
            _quotes.TryGetValue($"t{symbol.Replace("/", "")}", out var rate)
                ? rate
                : null;

        private void OnOpenEventHandler(object sender, EventArgs args)
        {
            IsAvailable = true;
            SubscribeToTickers();
        }

        private void OnCloseEventHandler(object sender, EventArgs e)
        {
            IsAvailable = false;
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
                _log?.LogError(e, "Bitfinex response handle error");
            }
        }

        private void HandleEvent(string msg)
        {
            if (!TryParseEvent(msg, out var eventMsg))
            {
                _log?.LogWarning("Unknown response type");
                return;
            }

            if (eventMsg.Event == "subscribed")
            {
                _channels[eventMsg.ChanId] = eventMsg.Symbol;
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
            else if (eventMsg.Event == "error")
            {
                _log?.LogError($"Bitfinex error with code: {eventMsg.Code} and message \"{eventMsg.Message}\"");
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

                _quotes[symbol] = new Quote
                {
                    Bid = jsonDocument.RootElement[1][0].GetDecimal(),
                    Ask = jsonDocument.RootElement[1][2].GetDecimal(),
                    TimeStamp = timeStamp
                };

                LastUpdateTime = timeStamp;
                LastSuccessUpdateTime = timeStamp;

                QuotesUpdated?.Invoke(this, EventArgs.Empty);
            }
            else
            {
                _log?.LogWarning($"Unknown channel id {chanId}");
            }
        }

        private void SubscribeToTickers()
        {
            foreach (var symbol in _quotes.Keys)
                _ws.Send($"{{" +
                    $"\"event\":\"subscribe\"," +
                    $"\"channel\":\"ticker\"," +
                    $"\"symbol\":\"{symbol}\"" +
                    $"}}");
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