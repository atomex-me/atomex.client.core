using System;
using System.Collections.Generic;
using System.Linq;
using Atomex.Core;
using Atomex.MarketData.Abstract;
using Newtonsoft.Json.Linq;
using Serilog;
using WebSocketSharp;
using ErrorEventArgs = WebSocketSharp.ErrorEventArgs;

namespace Atomex.MarketData.Bitfinex
{
    public class BitfinexWebSocketQuotesProvider : ICurrencyQuotesProvider
    {
        private const string BaseUrl = "wss://api-pub.bitfinex.com/ws/2";

        private readonly Dictionary<int, string> _channels;
        private readonly Dictionary<string, Quote> _quotes;
        private WebSocket _ws;

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

        public BitfinexWebSocketQuotesProvider(params string[] symbols)
        {
            _channels = new Dictionary<int, string>();
            _quotes = symbols.ToDictionary(s => s, s => new Quote());
        }

        public BitfinexWebSocketQuotesProvider(
            IEnumerable<Currency> currencies,
            string baseCurrency)
        {
            _channels = new Dictionary<int, string>();
            _quotes = currencies.ToDictionary(currency => $"t{currency.Name}{baseCurrency}", currency => new Quote());
        }

        public void Start()
        {
            _ws = new WebSocket(BaseUrl) { Log = { Output = (data, s) => { } } };
            _ws.OnOpen += OnOpenEventHandler;
            _ws.OnClose += OnCloseEventHandler;
            _ws.OnError += OnErrorEventHandler;
            _ws.OnMessage += OnMessageEventHandler;

            _ws.Connect();
        }

        public void Stop()
        {
            _ws.Close(CloseStatusCode.Normal);
        }

        public Quote GetQuote(string currency, string baseCurrency)
        {
            return _quotes.TryGetValue($"t{currency}{baseCurrency}", out var rate) ? rate : null;
        }

        private void OnOpenEventHandler(object sender, EventArgs args)
        {
            IsAvailable = true;
            SubscribeToTickers();
        }

        private void OnCloseEventHandler(object sender, CloseEventArgs args)
        {
            IsAvailable = false;
        }

        private void OnErrorEventHandler(object sender, ErrorEventArgs args)
        {
            Log.Error(args.Exception, "Bitfinex WebSocket error: {@message}", args.Message);
        }

        private void OnMessageEventHandler(object sender, MessageEventArgs args)
        {
            try
            {
                if (args.IsText)
                {
                    var response = JToken.Parse(args.Data);

                    if (response is JObject responseEvent)
                    {
                        HandleEvent(responseEvent);
                    }
                    else if (response is JArray responseData)
                    {
                        HandleData(responseData);
                    }

                    //Log.Debug(args.Data);
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
                _channels[response["chanId"].Value<int>()] = response["symbol"].Value<string>();
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
                if (response[1] is JArray ticker)
                {
                    var timeStamp = DateTime.Now;

                    _quotes[symbol] = new Quote
                    {
                        Bid = ticker[0].Value<decimal>(),
                        Ask = ticker[2].Value<decimal>(),
                        TimeStamp = timeStamp
                    };

                    LastUpdateTime = timeStamp;
                    LastSuccessUpdateTime = timeStamp;

                    QuotesUpdated?.Invoke(this, EventArgs.Empty);
                }
            }
            else
            {
                Log.Warning($"Unknown channel id {chanId}");
            }
        }

        private void SubscribeToTickers()
        {
            foreach (var symbol in _quotes.Keys)
            {
                _ws.Send($"{{ \"event\": \"subscribe\", \"channel\": \"ticker\", \"symbol\":\"{symbol}\"}}");
            }
        }
    }
}