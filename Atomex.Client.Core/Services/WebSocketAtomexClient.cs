using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Configuration;
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
    public class WebSocketAtomexClient : IAtomexClient
    {
        private static TimeSpan HeartBeatInterval = TimeSpan.FromSeconds(10);

        public event EventHandler<AtomexClientServiceEventArgs> ServiceConnected;
        public event EventHandler<AtomexClientServiceEventArgs> ServiceDisconnected;
        public event EventHandler<AtomexClientServiceEventArgs> ServiceAuthenticated;
        public event EventHandler<AtomexClientErrorEventArgs> Error;
        public event EventHandler<OrderEventArgs> OrderReceived;
        public event EventHandler<SwapEventArgs> SwapReceived;
        public event EventHandler<MarketDataEventArgs> QuotesUpdated;

        private CancellationTokenSource _exchangeCts;
        private Task _exchangeHeartBeatTask;

        private CancellationTokenSource _marketDataCts;
        private Task _marketDataHeartBeatTask;

        private ExchangeWebClient ExchangeClient { get; set; }
        private MarketDataWebClient MarketDataClient { get; set; }

        public IAccount Account { get; private set; }
        public IMarketDataRepository MarketDataRepository { get; private set; }
        private ISymbolsProvider SymbolsProvider { get; set; }
        private IConfiguration Configuration { get; }

        public WebSocketAtomexClient(
            IConfiguration configuration,
            IAccount account,
            ISymbolsProvider symbolsProvider)
        {
            Configuration        = configuration ?? throw new ArgumentNullException(nameof(configuration));
            Account              = account ?? throw new ArgumentNullException(nameof(account));
            SymbolsProvider      = symbolsProvider ?? throw new ArgumentNullException(nameof(symbolsProvider));
            MarketDataRepository = new MarketDataRepository();
        }

        public bool IsServiceConnected(AtomexClientService service)
        {
            return service switch
            {
                AtomexClientService.Exchange   => ExchangeClient.IsConnected,
                AtomexClientService.MarketData => MarketDataClient.IsConnected,
                AtomexClientService.All        => ExchangeClient.IsConnected && MarketDataClient.IsConnected,
                _ => throw new ArgumentOutOfRangeException(nameof(service), service, null),
            };
        }

        public async Task StartAsync()
        {
            try
            {
                Log.Information("Start terminal services");

                var configuration = Configuration.GetSection($"Services:{Account.Network}");

                // init schemes
                var schemes = new ProtoSchemes();

                // init market data repository
                MarketDataRepository.Initialize(SymbolsProvider.GetSymbols(Account.Network));

                // init exchange client
                ExchangeClient = new ExchangeWebClient(configuration, schemes);
                ExchangeClient.Connected     += OnExchangeConnectedEventHandler;
                ExchangeClient.Disconnected  += OnExchangeDisconnectedEventHandler;
                ExchangeClient.AuthOk        += OnExchangeAuthOkEventHandler;
                ExchangeClient.AuthNonce     += OnExchangeAuthNonceEventHandler;
                ExchangeClient.Error         += OnExchangeErrorEventHandler;
                ExchangeClient.OrderReceived += OnExchangeOrderEventHandler;
                ExchangeClient.SwapReceived  += OnSwapReceivedEventHandler;

                // init market data client
                MarketDataClient = new MarketDataWebClient(configuration, schemes);
                MarketDataClient.Connected        += OnMarketDataConnectedEventHandler;
                MarketDataClient.Disconnected     += OnMarketDataDisconnectedEventHandler;
                MarketDataClient.AuthOk           += OnMarketDataAuthOkEventHandler;
                MarketDataClient.AuthNonce        += OnMarketDataAuthNonceEventHandler;
                MarketDataClient.Error            += OnMarketDataErrorEventHandler;
                MarketDataClient.QuotesReceived   += OnQuotesReceivedEventHandler;
                MarketDataClient.EntriesReceived  += OnEntriesReceivedEventHandler;
                MarketDataClient.SnapshotReceived += OnSnapshotReceivedEventHandler;

                // start services
                var exchangeConnectTask = ExchangeClient.ConnectAsync();
                var marketDataConnectTask = MarketDataClient.ConnectAsync();

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
                if (ExchangeClient == null || MarketDataClient == null)
                    return;

                Log.Information("Stop terminal services");

                // close services
                await Task.WhenAll(ExchangeClient.CloseAsync(), MarketDataClient.CloseAsync())
                    .ConfigureAwait(false);

                ExchangeClient.Connected     -= OnExchangeConnectedEventHandler;
                ExchangeClient.Disconnected  -= OnExchangeDisconnectedEventHandler;
                ExchangeClient.AuthOk        -= OnExchangeAuthOkEventHandler;
                ExchangeClient.AuthNonce     -= OnExchangeAuthNonceEventHandler;
                ExchangeClient.Error         -= OnExchangeErrorEventHandler;
                ExchangeClient.OrderReceived -= OnExchangeOrderEventHandler;
                ExchangeClient.SwapReceived  -= OnSwapReceivedEventHandler;

                MarketDataClient.Connected        -= OnMarketDataConnectedEventHandler;
                MarketDataClient.Disconnected     -= OnMarketDataDisconnectedEventHandler;
                MarketDataClient.AuthOk           -= OnMarketDataAuthOkEventHandler;
                MarketDataClient.AuthNonce        -= OnMarketDataAuthNonceEventHandler;
                MarketDataClient.Error            -= OnMarketDataErrorEventHandler;
                MarketDataClient.QuotesReceived   -= OnQuotesReceivedEventHandler;
                MarketDataClient.EntriesReceived  -= OnEntriesReceivedEventHandler;
                MarketDataClient.SnapshotReceived -= OnSnapshotReceivedEventHandler;

                MarketDataRepository.Clear();
            }
            catch (Exception e)
            {
                Log.Error(e, "StopAsync error.");
            }
        }

        public async void OrderSendAsync(Order order)
        {
            // todo: mark used outputs as warranty outputs

            order.ClientOrderId = Guid.NewGuid().ToString();

            try
            {
                await Account
                    .UpsertOrderAsync(order)
                    .ConfigureAwait(false);

                ExchangeClient.OrderSendAsync(order);
            }
            catch (Exception e)
            {
                Log.Error(e, "Order send error");
            }
        }

        public void OrderCancelAsync(long id, string symbol, Side side) =>
            ExchangeClient.OrderCancelAsync(id, symbol, side);

        public void SubscribeToMarketData(SubscriptionType type) =>
            MarketDataClient.SubscribeAsync(new List<Subscription> { new Subscription { Type = type } });

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
                _exchangeHeartBeatTask = RunHeartBeatLoopAsync(ExchangeClient, _exchangeCts.Token);
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
                        nonce: ExchangeClient.Nonce,
                        keyIndex: Account.UserSettings.AuthenticationKeyIndex)
                    .ConfigureAwait(false);

                ExchangeClient.AuthAsync(auth);
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

                // resolve order wallets
                await order
                    .ResolveWallets(Account)
                    .ConfigureAwait(false);

                var result = await Account
                    .UpsertOrderAsync(order)
                    .ConfigureAwait(false);

                if (!result)
                    OnError(AtomexClientService.Exchange, "Error adding order");

                OrderReceived?.Invoke(this, args);
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
                _marketDataHeartBeatTask = RunHeartBeatLoopAsync(MarketDataClient, _marketDataCts.Token);
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
                        nonce: MarketDataClient.Nonce,
                        keyIndex: Account.UserSettings.AuthenticationKeyIndex)
                    .ConfigureAwait(false);

                MarketDataClient.AuthAsync(auth);
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
                var symbol = SymbolsProvider
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

            var symbol = SymbolsProvider
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

                SwapReceived?.Invoke(this, args);
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
            ExchangeClient.SwapInitiateAsync(id, secretHash, symbol, toAddress, rewardForRedeem, refundAddress);
        }

        public void SwapAcceptAsync(
            long id,
            string symbol,
            string toAddress,
            decimal rewardForRedeem,
            string refundAddress)
        {
            ExchangeClient.SwapAcceptAsync(id, symbol, toAddress, rewardForRedeem, refundAddress);
        }

        public void SwapStatusAsync(
            string requestId,
            long swapId)
        {
            ExchangeClient.SwapStatusAsync(requestId, swapId);
        }
    }
}
