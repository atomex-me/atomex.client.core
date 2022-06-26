using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Serilog;

using Atomex.Abstract;
using Atomex.Api.Proto;
using Atomex.Common;
using Atomex.Core;
using Atomex.MarketData;
using Atomex.MarketData.Abstract;
using Atomex.Services.Abstract;
using Atomex.Swaps;
using Atomex.Wallet.Abstract;
using Atomex.Web;

namespace Atomex.Services
{
    public class WebSocketAtomexClientLegacy : IAtomexClient
    {
        private static readonly TimeSpan HeartBeatInterval = TimeSpan.FromSeconds(10);

        public event EventHandler<AtomexClientServiceEventArgs> ServiceConnected;
        public event EventHandler<AtomexClientServiceEventArgs> ServiceDisconnected;
        public event EventHandler<AtomexClientServiceEventArgs> ServiceAuthenticated;
        public event EventHandler<AtomexClientErrorEventArgs> Error;
        public event EventHandler<OrderEventArgs> OrderUpdated;
        public event EventHandler<SwapEventArgs> SwapUpdated;
        public event EventHandler<MarketDataEventArgs> QuotesUpdated;

        private CancellationTokenSource _exchangeCts;
        private Task _exchangeHeartBeatTask;
        private CancellationTokenSource _marketDataCts;
        private Task _marketDataHeartBeatTask;
        private ExchangeWebClient _exchangeClient;
        private MarketDataWebClient _marketDataClient;
        private readonly ISymbolsProvider _symbolsProvider;
        private readonly string _exchangeUrl;
        private readonly string _marketDataUrl;
        private readonly AtomexClientOptions _options;

        public IAccount Account { get; private set; }
        public IMarketDataRepository MarketDataRepository { get; init; }

        public WebSocketAtomexClientLegacy(
            string exchangeUrl,
            string marketDataUrl,
            IAccount account,
            ISymbolsProvider symbolsProvider,
            AtomexClientOptions options = default)
        {
            _exchangeUrl = exchangeUrl ?? throw new ArgumentNullException(nameof(exchangeUrl));
            _marketDataUrl = marketDataUrl ?? throw new ArgumentNullException(nameof(marketDataUrl));
            _symbolsProvider = symbolsProvider ?? throw new ArgumentNullException(nameof(symbolsProvider));
            _options = options ?? AtomexClientOptions.DefaultOptions;

            Account = account ?? throw new ArgumentNullException(nameof(account));
            MarketDataRepository = new MarketDataRepository();
        }

        public bool IsServiceConnected(AtomexClientService service)
        {
            return service switch
            {
                AtomexClientService.Exchange   => _exchangeClient.IsConnected,
                AtomexClientService.MarketData => _marketDataClient.IsConnected,
                AtomexClientService.All        => _exchangeClient.IsConnected && _marketDataClient.IsConnected,
                _ => throw new ArgumentOutOfRangeException(nameof(service), service, null),
            };
        }

        public async Task StartAsync()
        {
            try
            {
                Log.Information("Start AtomexClient services");

                // init schemes
                var schemes = new ProtoSchemes();

                // init market data repository
                MarketDataRepository.Initialize(_symbolsProvider.GetSymbols(Account.Network));

                // init exchange client
                _exchangeClient = new ExchangeWebClient(_exchangeUrl, schemes);
                _exchangeClient.Connected     += OnExchangeConnectedEventHandler;
                _exchangeClient.Disconnected  += OnExchangeDisconnectedEventHandler;
                _exchangeClient.AuthOk        += OnExchangeAuthOkEventHandler;
                _exchangeClient.AuthNonce     += OnExchangeAuthNonceEventHandler;
                _exchangeClient.Error         += OnExchangeErrorEventHandler;
                _exchangeClient.OrderReceived += OnExchangeOrderEventHandler;
                _exchangeClient.SwapReceived  += OnSwapReceivedEventHandler;

                // init market data client
                _marketDataClient = new MarketDataWebClient(_marketDataUrl, schemes);
                _marketDataClient.Connected        += OnMarketDataConnectedEventHandler;
                _marketDataClient.Disconnected     += OnMarketDataDisconnectedEventHandler;
                _marketDataClient.AuthOk           += OnMarketDataAuthOkEventHandler;
                _marketDataClient.AuthNonce        += OnMarketDataAuthNonceEventHandler;
                _marketDataClient.Error            += OnMarketDataErrorEventHandler;
                _marketDataClient.QuotesReceived   += OnQuotesReceivedEventHandler;
                _marketDataClient.EntriesReceived  += OnEntriesReceivedEventHandler;
                _marketDataClient.SnapshotReceived += OnSnapshotReceivedEventHandler;

                // start services
                var exchangeConnectTask = _exchangeClient.ConnectAsync();
                var marketDataConnectTask = _marketDataClient.ConnectAsync();

                await Task.WhenAll(exchangeConnectTask, marketDataConnectTask)
                    .ConfigureAwait(false);
            }
            catch (Exception e)
            {
                Log.Error(e, "StartAsync error.");
            }
        }

        public async Task StopAsync()
        {
            try
            {
                if (_exchangeClient == null || _marketDataClient == null)
                    return;

                Log.Information("Stop AtomexClient services");

                // close services
                await Task.WhenAll(_exchangeClient.CloseAsync(), _marketDataClient.CloseAsync())
                    .ConfigureAwait(false);

                _exchangeClient.Connected     -= OnExchangeConnectedEventHandler;
                _exchangeClient.Disconnected  -= OnExchangeDisconnectedEventHandler;
                _exchangeClient.AuthOk        -= OnExchangeAuthOkEventHandler;
                _exchangeClient.AuthNonce     -= OnExchangeAuthNonceEventHandler;
                _exchangeClient.Error         -= OnExchangeErrorEventHandler;
                _exchangeClient.OrderReceived -= OnExchangeOrderEventHandler;
                _exchangeClient.SwapReceived  -= OnSwapReceivedEventHandler;

                _marketDataClient.Connected        -= OnMarketDataConnectedEventHandler;
                _marketDataClient.Disconnected     -= OnMarketDataDisconnectedEventHandler;
                _marketDataClient.AuthOk           -= OnMarketDataAuthOkEventHandler;
                _marketDataClient.AuthNonce        -= OnMarketDataAuthNonceEventHandler;
                _marketDataClient.Error            -= OnMarketDataErrorEventHandler;
                _marketDataClient.QuotesReceived   -= OnQuotesReceivedEventHandler;
                _marketDataClient.EntriesReceived  -= OnEntriesReceivedEventHandler;
                _marketDataClient.SnapshotReceived -= OnSnapshotReceivedEventHandler;

                MarketDataRepository.Clear();
            }
            catch (Exception e)
            {
                Log.Error(e, "StopAsync error.");
            }
        }

        public async void OrderSendAsync(Order order)
        {
            order.ClientOrderId = Guid.NewGuid().ToString();

            try
            {
                await Account
                    .UpsertOrderAsync(order)
                    .ConfigureAwait(false);

                _exchangeClient.OrderSendAsync(order);
            }
            catch (Exception e)
            {
                Log.Error(e, "Order send error");
            }
        }

        public void OrderCancelAsync(long id, string symbol, Side side) =>
            _exchangeClient.OrderCancelAsync(id, symbol, side);

        public void SubscribeToMarketData(SubscriptionType type) =>
            _marketDataClient.SubscribeAsync(new List<Subscription> { new Subscription { Type = type } });

        public MarketDataOrderBook GetOrderBook(string symbol) =>
            MarketDataRepository?.OrderBookBySymbol(symbol);

        public MarketDataOrderBook GetOrderBook(Symbol symbol) =>
            MarketDataRepository?.OrderBookBySymbol(symbol.Name);

        public Quote GetQuote(Symbol symbol) =>
            MarketDataRepository?.QuoteBySymbol(symbol.Name);

        #region ExchangeEventHandlers

        private void OnExchangeConnectedEventHandler(object sender, EventArgs args)
        {
            Log.Debug("Exchange client connected.");

            if (_exchangeHeartBeatTask == null ||
                _exchangeHeartBeatTask.IsCompleted ||
                _exchangeHeartBeatTask.IsCanceled ||
                _exchangeHeartBeatTask.IsFaulted)
            {
                Log.Debug("Run heartbeat for Exchange client.");

                _exchangeCts = new CancellationTokenSource();
                _exchangeHeartBeatTask = RunHeartBeatLoopAsync(_exchangeClient, _exchangeCts.Token);
            }

            ServiceConnected?.Invoke(this, new AtomexClientServiceEventArgs(AtomexClientService.Exchange));
        }

        private void OnExchangeDisconnectedEventHandler(object sender, EventArgs args)
        {
            Log.Debug("Exchange client disconnected.");

            if (_exchangeHeartBeatTask != null &&
                !_exchangeHeartBeatTask.IsCompleted &&
                !_exchangeHeartBeatTask.IsCanceled &&
                !_exchangeHeartBeatTask.IsFaulted)
            {
                try
                {
                    Log.Debug("Cancel Exchange client heartbeat.");
                    _exchangeCts.Cancel();
                }
                catch (OperationCanceledException)
                {
                    Log.Debug("Exchange heart beat loop canceled.");
                }
            }

            ServiceDisconnected?.Invoke(this, new AtomexClientServiceEventArgs(AtomexClientService.Exchange));
        }

        private void OnExchangeAuthOkEventHandler(object sender, EventArgs e) =>
            ServiceAuthenticated?.Invoke(this, new AtomexClientServiceEventArgs(AtomexClientService.Exchange));

        private async void OnExchangeAuthNonceEventHandler(object sender, EventArgs args)
        {
            try
            {
                var auth = await Account
                    .CreateAuthRequestAsync(
                        nonce: _exchangeClient.Nonce,
                        keyIndex: Account.UserData.AuthenticationKeyIndex)
                    .ConfigureAwait(false);

                _exchangeClient.AuthAsync(auth);
            }
            catch (Exception e)
            {
                Log.Error(e, "Exchange auth error");
            }
        }

        private void OnExchangeErrorEventHandler(object sender, ErrorEventArgs args)
        {
            Log.Error("Exchange service error {@Error}", args.Error);
            Error?.Invoke(this, new AtomexClientErrorEventArgs(AtomexClientService.Exchange, args.Error));
        }

        private async void OnExchangeOrderEventHandler(object sender, OrderEventArgs args)
        {
            // todo: remove warranty outputs if cancel/rejected/partially_filled/filled

            var order = args.Order;

            try
            {
                if (order.Status == OrderStatus.Pending)
                {
                    OnError(AtomexClientService.Exchange, $"Invalid order status {order.Status}");
                    return;
                }

                // remove canceled orders without trades from local db if StoreCanceledOrders options is true
                if (order.Status == OrderStatus.Canceled && order.LastQty == 0 && !_options.StoreCanceledOrders)
                {
                    await Account
                        .RemoveOrderByIdAsync(order.Id)
                        .ConfigureAwait(false);
                }
                else
                {
                    // resolve order wallets
                    await order
                        .ResolveWallets(Account)
                        .ConfigureAwait(false);

                    var upsertResult = await Account
                        .UpsertOrderAsync(order)
                        .ConfigureAwait(false);

                    if (!upsertResult)
                        OnError(AtomexClientService.Exchange, "Error adding order");
                }

                OrderUpdated?.Invoke(this, args);
            }
            catch (Exception e)
            {
                OnError(AtomexClientService.Exchange, e);
            }
        }

        #endregion

        #region MarketDataEventHandlers

        private void OnMarketDataConnectedEventHandler(object sender, EventArgs args)
        {
            Log.Debug("MarketData client connected.");

            if (_marketDataHeartBeatTask == null ||
                _marketDataHeartBeatTask.IsCompleted ||
                _marketDataHeartBeatTask.IsCanceled ||
                _marketDataHeartBeatTask.IsFaulted)
            {
                Log.Debug("Run heartbeat for MarketData client.");

                _marketDataCts = new CancellationTokenSource();
                _marketDataHeartBeatTask = RunHeartBeatLoopAsync(_marketDataClient, _marketDataCts.Token);
            }

            ServiceConnected?.Invoke(this, new AtomexClientServiceEventArgs(AtomexClientService.MarketData));
        }

        private void OnMarketDataDisconnectedEventHandler(object sender, EventArgs args)
        {
            Log.Debug("MarketData client disconnected.");

            if (_marketDataHeartBeatTask != null &&
                !_marketDataHeartBeatTask.IsCompleted &&
                !_marketDataHeartBeatTask.IsCanceled &&
                !_marketDataHeartBeatTask.IsFaulted)
            {
                try
                {
                    Log.Debug("Cancel MarketData client heartbeat.");
                    _marketDataCts.Cancel();
                }
                catch (OperationCanceledException)
                {
                    Log.Debug("Exchange heart beat loop canceled.");
                }
            }

            ServiceDisconnected?.Invoke(this, new AtomexClientServiceEventArgs(AtomexClientService.MarketData));
        }

        private void OnMarketDataAuthOkEventHandler(object sender, EventArgs e) =>
            ServiceAuthenticated?.Invoke(this, new AtomexClientServiceEventArgs(AtomexClientService.MarketData));

        private async void OnMarketDataAuthNonceEventHandler(object sender, EventArgs args)
        {
            try
            {
                var auth = await Account
                    .CreateAuthRequestAsync(
                        nonce: _marketDataClient.Nonce,
                        keyIndex: Account.UserData.AuthenticationKeyIndex)
                    .ConfigureAwait(false);

                _marketDataClient.AuthAsync(auth);
            }
            catch (Exception e)
            {
                Log.Error(e, "MarketData auth error");
            }
        }

        private void OnMarketDataErrorEventHandler(object sender, ErrorEventArgs args)
        {
            Log.Warning("Market data service error {@Error}", args.Error);

            Error?.Invoke(this, new AtomexClientErrorEventArgs(AtomexClientService.Exchange, args.Error));
        }

        private void OnQuotesReceivedEventHandler(object sender, QuotesEventArgs args)
        {
            Log.Verbose("Quotes: {@quotes}", args.Quotes);

            MarketDataRepository.ApplyQuotes(args.Quotes);

            var symbolsIds = new HashSet<string>();

            foreach (var quote in args.Quotes)
            {
                if (!symbolsIds.Contains(quote.Symbol))
                    symbolsIds.Add(quote.Symbol);
            }

            foreach (var symbolId in symbolsIds)
            {
                var symbol = _symbolsProvider
                    .GetSymbols(Account.Network)
                    .GetByName(symbolId);

                if (symbol != null)
                    QuotesUpdated?.Invoke(this, new MarketDataEventArgs(symbol));
            }
        }

        private void OnEntriesReceivedEventHandler(object sender, EntriesEventArgs args)
        {
            Log.Verbose("Entries: {@entries}", args.Entries);

            MarketDataRepository.ApplyEntries(args.Entries);
        }

        private void OnSnapshotReceivedEventHandler(object sender, SnapshotEventArgs args)
        {
            Log.Verbose("Snapshot: {@snapshot}", args.Snapshot);

            MarketDataRepository.ApplySnapshot(args.Snapshot);

            var symbol = _symbolsProvider
                .GetSymbols(Account.Network)
                .GetByName(args.Snapshot.Symbol);

            if (symbol != null)
                QuotesUpdated?.Invoke(this, new MarketDataEventArgs(symbol));
        }

        #endregion

        #region SwapEventHandlers

        private void OnSwapReceivedEventHandler(object sender, SwapEventArgs args)
        {
            try
            {
                if (args.Swap == null)
                {
                    OnError(AtomexClientService.Exchange, "Null swap received.");
                    return;
                }

                SwapUpdated?.Invoke(this, args);
            }
            catch (Exception e)
            {
                OnError(AtomexClientService.Exchange, e);
            }
        }

        #endregion

        private void OnError(AtomexClientService service, string description)
        {
            Log.Error(description);
            Error?.Invoke(this, new AtomexClientErrorEventArgs(service, new Error(Errors.InternalError, description)));
        }

        private void OnError(AtomexClientService service, Exception exception)
        {
            Log.Error(exception, exception.Message);
            Error?.Invoke(this, new AtomexClientErrorEventArgs(service, new Error(Errors.InternalError, exception.Message)));
        }

        private async Task RunHeartBeatLoopAsync(
            BinaryWebSocketClient webSocketClient,
            CancellationToken cancellationToken = default)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    webSocketClient.SendHeartBeatAsync();

                    await Task.Delay(HeartBeatInterval, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    Log.Debug("HeartBeat loop canceled.");
                }
                catch (Exception e)
                {
                    Log.Error(e, "Error while sending heartbeat.");
                }
            }

            Log.Debug("Heartbeat stopped.");
        }

        public void SwapInitiateAsync(
            long id,
            byte[] secretHash,
            string symbol,
            string toAddress,
            decimal rewardForRedeem,
            string refundAddress)
        {
            _exchangeClient.SwapInitiateAsync(id, secretHash, symbol, toAddress, rewardForRedeem, refundAddress);
        }

        public void SwapAcceptAsync(
            long id,
            string symbol,
            string toAddress,
            decimal rewardForRedeem,
            string refundAddress)
        {
            _exchangeClient.SwapAcceptAsync(id, symbol, toAddress, rewardForRedeem, refundAddress);
        }

        public void SwapStatusAsync(
            string requestId,
            long swapId)
        {
            _exchangeClient.SwapStatusAsync(requestId, swapId);
        }
    }
}
