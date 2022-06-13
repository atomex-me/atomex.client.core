using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;
using System.Net.Http;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using Serilog;

using Atomex.Core;
using Atomex.MarketData.Abstract;

namespace Atomex.MarketData.Binance
{
    public class BinanceRestOrderBooksProvider : ICurrencyOrderBookProvider
    {
        private readonly Dictionary<string, string> Symbols = new()
        {
            { "ETH/BTC", "ETHBTC" },
            { "LTC/BTC", "LTCBTC" },
            { "XTZ/BTC", "XTZBTC" },
            { "XTZ/ETH", "XTZETH" }, // syntetic

            { "BTC/USDT", "BTCUSDT" },
            { "ETH/USDT", "ETHUSDT" },
            { "LTC/USDT", "LTCUSDT" },
            { "XTZ/USDT", "XTZUSDT" },

            { "ETH/TZBTC", "ETHBTC" },
            { "XTZ/TZBTC", "XTZBTC" },
            { "TZBTC/USDT", "BTCUSDT" },

            { "ETH/TBTC", "ETHBTC" },
            { "XTZ/TBTC", "XTZBTC" },
            { "TBTC/USDT", "BTCUSDT" },

            { "ETH/WBTC", "ETHBTC" },
            { "XTZ/WBTC", "XTZBTC" },
            { "WBTC/USDT", "BTCUSDT" },

            { "BTC/KUSD", "BTCUSDT" },
            { "ETH/KUSD", "ETHUSDT" },
            { "LTC/KUSD", "LTCUSDT" },
            { "XTZ/KUSD", "XTZUSDT" },
            { "TZBTC/KUSD", "BTCUSDT" },
        };

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

        public event EventHandler<OrderBookEventArgs> OrderBookUpdated;
        public event EventHandler AvailabilityChanged;

        private readonly Dictionary<string, MarketDataOrderBook> _orderbooks;
        private readonly string[] _symbols;
        private readonly HttpClient _httpClient;

        private CancellationTokenSource _cts;
        private Task _updateTask;
        private TimeSpan _updateInterval = TimeSpan.FromSeconds(5);

        public bool IsRunning => _updateTask != null &&
                                 !_updateTask.IsCompleted &&
                                 !_updateTask.IsCanceled &&
                                 !_updateTask.IsFaulted;

        public string Name => "Binance RestApi";

        public BinanceRestOrderBooksProvider(params string[] symbols)
        {
            _httpClient = new HttpClient { BaseAddress = new Uri("https://api.binance.com/") };

            //_depth = depth;
            _symbols = symbols;

            _orderbooks = symbols
                .Select(s => Symbols[s])
                .Distinct()
                .ToDictionary(s => s, s => new MarketDataOrderBook(s));
        }

        public void Start()
        {
            _cts = new CancellationTokenSource();
            _updateTask = Task.Run(Run, _cts.Token);
        }

        private async Task Run()
        {
            IsAvailable = true;

            try
            {
                while (!_cts.IsCancellationRequested)
                {
                    foreach (var symbol in _symbols)
                    {
                        var binanceSymbol = Symbols[symbol];

                        var response = await _httpClient
                            .GetAsync($"api/v3/ticker/bookTicker?symbol={binanceSymbol}")
                            .ConfigureAwait(false);

                        var responseContent = await response.Content
                            .ReadAsStringAsync()
                            .ConfigureAwait(false);

                        var bookTicker = JsonConvert.DeserializeObject<JObject>(responseContent);
                        var bid = bookTicker["bidPrice"].Value<decimal>();
                        var ask = bookTicker["askPrice"].Value<decimal>();
                        var bidQty = bookTicker["bidQty"].Value<decimal>();
                        var askQty = bookTicker["askQty"].Value<decimal>();

                        if (_orderbooks.TryGetValue(binanceSymbol, out var orderBook))
                        {
                            var topOfBook = orderBook.TopOfBook();

                            if (ask != topOfBook.Ask || bid != topOfBook.Bid)
                            {
                                orderBook.ApplySnapshot(new Snapshot
                                {
                                    Symbol = symbol,
                                    Entries = new List<Entry>
                                    {
                                        new Entry
                                        {
                                            Side          = Side.Buy,
                                            Symbol        = symbol,
                                            Price         = bid,
                                            QtyProfile    = new List<decimal> { bidQty },
                                            TransactionId = 0
                                        },
                                        new Entry
                                        {
                                            Side          = Side.Sell,
                                            Symbol        = symbol,
                                            Price         = ask,
                                            QtyProfile    = new List<decimal> { askQty },
                                            TransactionId = 0
                                        },
                                    },
                                    LastTransactionId = 0
                                });

                                LastUpdateTime = DateTime.Now;
                                OrderBookUpdated?.Invoke(this, new OrderBookEventArgs(orderBook));

                                Log.Debug($"Symbol: {symbol}; Bid: {bid}; Ask: {ask}");
                            }
                        }

                        await Task.Delay(200)
                            .ConfigureAwait(false);
                    }

                    await Task.Delay(_updateInterval)
                        .ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                Log.Debug("Update task canceled");
            }
            catch (Exception e)
            {
                Log.Error(e, "Update task error");
            }
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

        public MarketDataOrderBook GetOrderBook(string currency, string quoteCurrency)
        {
            if (!Symbols.TryGetValue($"{currency}/{quoteCurrency}", out var symbol))
                return null;

            return _orderbooks.TryGetValue(symbol, out var orderbook) ? orderbook : null;
        }
    }
}