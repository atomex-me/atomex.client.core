using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;

using Atomex.Common;
using Atomex.Client.Abstract;
using Atomex.Client.Common;
using Atomex.Client.V1;
using Atomex.Client.V1.Common;
using Atomex.Client.V1.Entities;
using Atomex.Client.V1.Proto;
using Atomex.MarketData;
using Atomex.MarketData.Abstract;
using Atomex.MarketData.Common;
using Atomex.MarketData.Entities;
using Error = Atomex.Common.Error;

namespace Atomex.Services
{
    public class WebSocketAtomexClient : IAtomexClient
    {
        private static readonly TimeSpan HeartBeatInterval = TimeSpan.FromSeconds(10);

        public event EventHandler<ServiceEventArgs> ServiceStatusChanged;
        public event EventHandler<ServiceErrorEventArgs> Error;
        public event EventHandler<OrderEventArgs> OrderReceived;
        public event EventHandler<SwapEventArgs> SwapReceived;
        public event EventHandler<MarketDataEventArgs> QuotesUpdated;

        private CancellationTokenSource _exchangeCts;
        private CancellationTokenSource _marketDataCts;
        private Task _exchangeHeartBeatTask;
        private Task _marketDataHeartBeatTask;
        private ExchangeWebClient _exchangeClient;
        private MarketDataWebClient _marketDataClient;
        private readonly ILogger _log;
        private readonly string _exchangeUrl;
        private readonly string _marketDataUrl;
        private readonly Func<AuthNonce, Task<Auth>> _signAuthDataCallback;

        //public IExchangeDataRepository ExchangeDataRepository { get; private set; }
        //public IMarketDataRepository MarketDataRepository { get; private set; }

        public WebSocketAtomexClient(
            string exchangeUrl,
            string marketDataUrl,
            //IExchangeDataRepository exchangeDataRepository,
            IMarketDataRepository marketDataRepository,
            Func<AuthNonce, Task<Auth>> signAuthData,
            ILogger log = null)
        {
            _exchangeUrl = exchangeUrl ?? throw new ArgumentNullException(nameof(exchangeUrl));
            _marketDataUrl = marketDataUrl ?? throw new ArgumentNullException(nameof(marketDataUrl));
            _signAuthDataCallback = signAuthData ?? throw new ArgumentNullException(nameof(signAuthData));
            _log = log;

            //ExchangeDataRepository = exchangeDataRepository
            //    ?? throw new ArgumentNullException(nameof(exchangeDataRepository));
            //MarketDataRepository = marketDataRepository
            //    ?? throw new ArgumentNullException(nameof(marketDataRepository));
        }

        public bool IsServiceConnected(Service service)
        {
            return service switch
            {
                Service.Exchange   => _exchangeClient.IsConnected,
                Service.MarketData => _marketDataClient.IsConnected,
                Service.All        => _exchangeClient.IsConnected && _marketDataClient.IsConnected,
                _ => throw new ArgumentOutOfRangeException(nameof(service), service, null),
            };
        }

        public async Task StartAsync()
        {
            try
            {
                _log.LogInformation("Start AtomexClient services");

                // init schemes
                var schemes = new ProtoSchemes();

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

                await Task
                    .WhenAll(exchangeConnectTask, marketDataConnectTask)
                    .ConfigureAwait(false);
            }
            catch (Exception e)
            {
                _log.LogError(e, "StartAsync error");
            }
        }

        public async Task StopAsync()
        {
            try
            {
                if (_exchangeClient == null || _marketDataClient == null)
                    return;

                _log.LogInformation("Stop AtomexClient services");

                // close services
                await Task
                    .WhenAll(_exchangeClient.CloseAsync(), _marketDataClient.CloseAsync())
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
                _log.LogError(e, "StopAsync error");
            }
        }

        public async void OrderSendAsync(Order order)
        {
            //order.ClientOrderId = Guid.NewGuid().ToString();

            try
            {
                //await ExchangeDataRepository
                //    .UpsertOrderAsync(order)
                //    .ConfigureAwait(false);

                _exchangeClient.OrderSendAsync(order);
            }
            catch (Exception e)
            {
                _log.LogError(e, "Order send error");
            }
        }

        public void OrderCancelAsync(long id, string symbol, Side side) =>
            _exchangeClient.OrderCancelAsync(id, symbol, side);

        public void SubscribeToMarketData(SubscriptionType type) =>
            _marketDataClient.SubscribeAsync(new List<Subscription> {
                new Subscription { Type = type }
            });

        public OrderBook GetOrderBook(string symbol) =>
            MarketDataRepository?.OrderBookBySymbol(symbol);

        public Quote GetQuote(string symbol) =>
            MarketDataRepository?.QuoteBySymbol(symbol);

        #region ExchangeEventHandlers

        private void OnExchangeConnectedEventHandler(object sender, EventArgs args)
        {
            _log.LogDebug("Exchange client connected");

            if (_exchangeHeartBeatTask == null ||
                _exchangeHeartBeatTask.IsCompleted ||
                _exchangeHeartBeatTask.IsCanceled ||
                _exchangeHeartBeatTask.IsFaulted)
            {
                _log.LogDebug("Run heartbeat for Exchange client");

                _exchangeCts = new CancellationTokenSource();
                _exchangeHeartBeatTask = RunHeartBeatLoopAsync(
                    webSocketClient: _exchangeClient,
                    cancellationToken: _exchangeCts.Token);
            }

            ServiceStatusChanged?.Invoke(this, new ServiceEventArgs(
                service: Service.Exchange,
                status: ServiceStatus.Connected));
        }

        private void OnExchangeDisconnectedEventHandler(object sender, EventArgs args)
        {
            _log.LogDebug("Exchange client disconnected");

            if (_exchangeHeartBeatTask != null &&
                !_exchangeHeartBeatTask.IsCompleted &&
                !_exchangeHeartBeatTask.IsCanceled &&
                !_exchangeHeartBeatTask.IsFaulted)
            {
                try
                {
                    _log.LogDebug("Cancel Exchange client heartbeat");
                    _exchangeCts.Cancel();
                }
                catch (OperationCanceledException)
                {
                    _log.LogDebug("Exchange heart beat loop canceled");
                }
            }

            ServiceStatusChanged?.Invoke(this, new ServiceEventArgs(
                service: Service.Exchange,
                status: ServiceStatus.Disconnected));
        }

        private void OnExchangeAuthOkEventHandler(object sender, EventArgs e) =>
            ServiceStatusChanged?.Invoke(this, new ServiceEventArgs(
                service: Service.Exchange,
                status: ServiceStatus.Authenticated));

        private async void OnExchangeAuthNonceEventHandler(object sender, NonceEventArgs args)
        {
            try
            {
                var auth = await _signAuthDataCallback
                    .Invoke(args.Nonce)
                    .ConfigureAwait(false);

                _exchangeClient.AuthAsync(auth);
            }
            catch (Exception e)
            {
                _log.LogError(e, "Exchange auth error");
            }
        }

        private void OnExchangeErrorEventHandler(object sender, ErrorEventArgs args)
        {
            _log.LogError("Exchange service error {@Error}", args.Error);

            Error?.Invoke(this, new ServiceErrorEventArgs(args.Error, Service.Exchange));
        }

        private void OnExchangeOrderEventHandler(object sender, OrderEventArgs args)
        {
            try
            {
                //var storedOrder = await ExchangeDataRepository
                //    .GetOrderByIdAsync(args.Order.ClientOrderId)
                //    .ConfigureAwait(false);

                //// TODO: verify order and forward new parameters
                //// Validate(args.Order, storedOrder);
                //// ForwardParameters(args.Order, storedOrder);

                //var result = await ExchangeDataRepository
                //    .UpsertOrderAsync(storedOrder)
                //    .ConfigureAwait(false);

                //if (!result)
                //    OnError(Service.Exchange, "Error adding order");

                OrderReceived?.Invoke(this, args);
            }
            catch (Exception e)
            {
                OnError(Service.Exchange, e);
            }
        }

        #endregion

        #region MarketDataEventHandlers

        private void OnMarketDataConnectedEventHandler(object sender, EventArgs args)
        {
            _log.LogDebug("MarketData client connected");

            if (_marketDataHeartBeatTask == null ||
                _marketDataHeartBeatTask.IsCompleted ||
                _marketDataHeartBeatTask.IsCanceled ||
                _marketDataHeartBeatTask.IsFaulted)
            {
                _log.LogDebug("Run heartbeat for MarketData client");

                _marketDataCts = new CancellationTokenSource();
                _marketDataHeartBeatTask = RunHeartBeatLoopAsync(
                    webSocketClient: _marketDataClient,
                    cancellationToken: _marketDataCts.Token);
            }

            ServiceStatusChanged?.Invoke(this, new ServiceEventArgs(
                service: Service.MarketData,
                status: ServiceStatus.Connected));
        }

        private void OnMarketDataDisconnectedEventHandler(object sender, EventArgs args)
        {
            _log.LogDebug("MarketData client disconnected");

            if (_marketDataHeartBeatTask != null &&
                !_marketDataHeartBeatTask.IsCompleted &&
                !_marketDataHeartBeatTask.IsCanceled &&
                !_marketDataHeartBeatTask.IsFaulted)
            {
                try
                {
                    _log.LogDebug("Cancel MarketData client heartbeat");
                    _marketDataCts.Cancel();
                }
                catch (OperationCanceledException)
                {
                    _log.LogDebug("Exchange heart beat loop canceled");
                }
            }

            ServiceStatusChanged?.Invoke(this, new ServiceEventArgs(
                service: Service.MarketData,
                status: ServiceStatus.Disconnected));
        }

        private void OnMarketDataAuthOkEventHandler(object sender, EventArgs e) =>
            ServiceStatusChanged?.Invoke(this, new ServiceEventArgs(
                service: Service.MarketData,
                status: ServiceStatus.Authenticated));

        private async void OnMarketDataAuthNonceEventHandler(object sender, NonceEventArgs args)
        {
            try
            {
                var auth = await _signAuthDataCallback
                    .Invoke(args.Nonce)
                    .ConfigureAwait(false);

                _marketDataClient.AuthAsync(auth);
            }
            catch (Exception e)
            {
                _log.LogError(e, "MarketData auth error");
            }
        }

        private void OnMarketDataErrorEventHandler(object sender, ErrorEventArgs args)
        {
            _log.LogWarning("Market data service error {@Error}", args.Error);

            Error?.Invoke(this, new ServiceErrorEventArgs(args.Error, Service.Exchange));
        }

        private void OnQuotesReceivedEventHandler(object sender, QuotesEventArgs args)
        {
            _log.LogTrace("Quotes: {@quotes}", args.Quotes);

            MarketDataRepository.ApplyQuotes(args.Quotes);

            var symbolsIds = new HashSet<string>();

            foreach (var quote in args.Quotes)
                if (!symbolsIds.Contains(quote.Symbol))
                    symbolsIds.Add(quote.Symbol);

            foreach (var symbolId in symbolsIds)
                QuotesUpdated?.Invoke(this, new MarketDataEventArgs(symbolId));
        }

        private void OnEntriesReceivedEventHandler(object sender, EntriesEventArgs args)
        {
            _log.LogTrace("Entries: {@entries}", args.Entries);

            MarketDataRepository.ApplyEntries(args.Entries);
        }

        private void OnSnapshotReceivedEventHandler(object sender, SnapshotEventArgs args)
        {
            _log.LogTrace("Snapshot: {@snapshot}", args.Snapshot);

            MarketDataRepository.ApplySnapshot(args.Snapshot);

            QuotesUpdated?.Invoke(this, new MarketDataEventArgs(args.Snapshot.Symbol));
        }

        #endregion

        #region SwapEventHandlers

        private void OnSwapReceivedEventHandler(object sender, SwapEventArgs args)
        {
            try
            {
                if (args.Swap == null)
                {
                    OnError(Service.Exchange, "Null swap received");
                    return;
                }

                SwapReceived?.Invoke(this, args);
            }
            catch (Exception e)
            {
                OnError(Service.Exchange, e);
            }
        }

        #endregion

        private void OnError(Service service, string description)
        {
            _log.LogError(description);

            Error?.Invoke(this, new ServiceErrorEventArgs(
                error: new Error(Errors.InternalError, description),
                service: service));
        }

        private void OnError(Service service, Exception exception)
        {
            _log.LogError(exception, exception.Message);

            Error?.Invoke(this, new ServiceErrorEventArgs(
                error: new Error(Errors.InternalError, exception.Message),
                service: service));
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

                    await Task
                        .Delay(HeartBeatInterval, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    _log.LogDebug("HeartBeat loop canceled");
                }
                catch (Exception e)
                {
                    _log.LogError(e, "Error while sending heartbeat");
                }
            }

            _log.LogDebug("Heartbeat stopped");
        }

        public void SwapInitiateAsync(
            long id,
            byte[] secretHash,
            string symbol,
            string toAddress,
            decimal rewardForRedeem,
            string refundAddress)
        {
            _exchangeClient.SwapInitiateAsync(
                id,
                secretHash,
                symbol,
                toAddress,
                rewardForRedeem,
                refundAddress);
        }

        public void SwapAcceptAsync(
            long id,
            string symbol,
            string toAddress,
            decimal rewardForRedeem,
            string refundAddress)
        {
            _exchangeClient.SwapAcceptAsync(
                id,
                symbol,
                toAddress,
                rewardForRedeem,
                refundAddress);
        }

        public void SwapStatusAsync(
            string requestId,
            long swapId)
        {
            _exchangeClient.SwapStatusAsync(requestId, swapId);
        }
    }
}