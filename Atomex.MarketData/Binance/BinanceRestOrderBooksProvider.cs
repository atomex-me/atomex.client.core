using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Threading;

using Microsoft.Extensions.Logging;

using Atomex.Common;
using Atomex.MarketData.Abstract;
using Atomex.MarketData.Common;
using Atomex.MarketData.Entities;

namespace Atomex.MarketData.Binance
{
    public class BinanceRestOrderBooksProvider : IOrderBookProvider
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

            { "BTC/USDT_XTZ", "BTCUSDT" },
            { "ETH/USDT_XTZ", "ETHUSDT" },
            { "LTC/USDT_XTZ", "LTCUSDT" },
            { "XTZ/USDT_XTZ", "XTZUSDT" },
            { "TZBTC/USDT_XTZ", "BTCUSDT" },
        };

        private const string BaseUrl = "https://api.binance.com/";

        public event EventHandler<OrderBookEventArgs> OrderBookUpdated;
        public event EventHandler AvailabilityChanged;

        public DateTime LastUpdateTime { get; private set; }
        public bool IsRunning => _updateTask != null &&
            !_updateTask.IsCompleted &&
            !_updateTask.IsCanceled &&
            !_updateTask.IsFaulted;

        public string Name => "Binance RestApi";

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

        private readonly Dictionary<string, OrderBook> _orderbooks;
        private readonly string[] _symbols;
        private readonly TimeSpan _updateInterval = TimeSpan.FromSeconds(5);
        private readonly TimeSpan _updateIntervalBetweenSymbols = TimeSpan.FromMilliseconds(200);
        private readonly ILogger _log;
        private CancellationTokenSource _cts;
        private Task _updateTask;

        public BinanceRestOrderBooksProvider(
            ILogger log = null,
            params string[] symbols)
        {
            _log = log;
            _symbols = symbols;

            _orderbooks = symbols
                .Select(s => Symbols[s])
                .Distinct()
                .ToDictionary(s => s, s => new OrderBook(s));
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

                        using var response = await HttpHelper
                            .GetAsync(
                                baseUri: BaseUrl,
                                relativeUri: $"api/v3/ticker/bookTicker?symbol={binanceSymbol}",
                                cancellationToken: _cts.Token)
                            .ConfigureAwait(false);

                        var responseContent = await response
                            .Content
                            .ReadAsStringAsync()
                            .ConfigureAwait(false);

                        var bookTicker = JsonSerializer.Deserialize<BinanceBookTicker>(responseContent);// JsonConvert.DeserializeObject<JObject>(responseContent);
                        //var bid = bookTicker["bidPrice"].Value<decimal>();
                        //var ask = bookTicker["askPrice"].Value<decimal>();
                        //var bidQty = bookTicker["bidQty"].Value<decimal>();
                        //var askQty = bookTicker["askQty"].Value<decimal>();

                        if (_orderbooks.TryGetValue(binanceSymbol, out var orderBook))
                        {
                            var topOfBook = orderBook.TopOfBook();

                            if (bookTicker.Ask != topOfBook.Ask || bookTicker.Bid != topOfBook.Bid)
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
                                            Price         = bookTicker.Bid,
                                            QtyProfile    = new List<decimal> { bookTicker.BidQty },
                                            TransactionId = 0
                                        },
                                        new Entry
                                        {
                                            Side          = Side.Sell,
                                            Symbol        = symbol,
                                            Price         = bookTicker.Ask,
                                            QtyProfile    = new List<decimal> { bookTicker.AskQty },
                                            TransactionId = 0
                                        },
                                    },
                                    LastTransactionId = 0
                                });

                                LastUpdateTime = DateTime.Now;
                                OrderBookUpdated?.Invoke(this, new OrderBookEventArgs(orderBook));

                                _log?.LogDebug($"Symbol: {symbol}; Bid: {bookTicker.Bid}; Ask: {bookTicker.Ask}");
                            }
                        }

                        await Task.Delay(_updateIntervalBetweenSymbols, _cts.Token)
                            .ConfigureAwait(false);
                    }

                    await Task.Delay(_updateInterval, _cts.Token)
                        .ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                _log?.LogDebug("Update task canceled");
            }
            catch (Exception e)
            {
                _log?.LogError(e, "Update task error");
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
                _log?.LogWarning("OrderBook provider task already finished");
            }
        }

        public OrderBook GetOrderBook(string currency, string quoteCurrency) =>
            Symbols.TryGetValue($"{currency}/{quoteCurrency}", out var symbol)
                ? _orderbooks.TryGetValue(symbol, out var orderbook) ? orderbook : null
                : null;
    }
}