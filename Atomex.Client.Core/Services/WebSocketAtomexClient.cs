using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Configuration;
using Serilog;

using Atomex.Abstract;
using Atomex.Api.Proto;
using Atomex.Blockchain;
using Atomex.Blockchain.Abstract;
using Atomex.Blockchain.Helpers;
using Atomex.Common;
using Atomex.Core;
using Atomex.MarketData;
using Atomex.MarketData.Abstract;
using Atomex.Services.Abstract;
using Atomex.Swaps;
using Atomex.Swaps.Abstract;
using Atomex.Wallet.Abstract;
using Atomex.Web;
using Atomex.Wallet;
using Atomex.Blockchain.Tezos;
using Atomex.Wallet.Tezos;

namespace Atomex.Services
{
    public class WebSocketAtomexClient : IAtomexClient
    {
        protected static TimeSpan DefaultMaxTransactionTimeout = TimeSpan.FromMinutes(48 * 60);
        private static TimeSpan HeartBeatInterval = TimeSpan.FromSeconds(10);

        public event EventHandler<TerminalServiceEventArgs> ServiceConnected;
        public event EventHandler<TerminalServiceEventArgs> ServiceDisconnected;
        public event EventHandler<TerminalServiceEventArgs> ServiceAuthenticated;
        public event EventHandler<TerminalErrorEventArgs> Error;
        public event EventHandler<OrderEventArgs> OrderReceived;
        public event EventHandler<MarketDataEventArgs> QuotesUpdated;
        public event EventHandler<SwapEventArgs> SwapUpdated;

        private readonly CancellationTokenSource _cts;

        private CancellationTokenSource _exchangeCts;
        private Task _exchangeHeartBeatTask;

        private CancellationTokenSource _marketDataCts;
        private Task _marketDataHeartBeatTask;

        private ExchangeWebClient ExchangeClient { get; set; }
        private MarketDataWebClient MarketDataClient { get; set; }

        public IAccount Account { get; set; }
        private ISymbolsProvider SymbolsProvider { get; set; }
        private ICurrencyQuotesProvider QuotesProvider { get; set; }
        private IConfiguration Configuration { get; }
        private IMarketDataRepository MarketDataRepository { get; set; }
        private ISwapManager SwapManager { get; set; }

        private TimeSpan TransactionConfirmationCheckInterval(string currency) =>
            currency == "BTC"
                ? TimeSpan.FromSeconds(120)
                : TimeSpan.FromSeconds(45);

        public WebSocketAtomexClient(
            IConfiguration configuration,
            IAccount account,
            ISymbolsProvider symbolsProvider,
            ICurrencyQuotesProvider quotesProvider = null)
        {
            Configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));

            Account = account ?? throw new ArgumentNullException(nameof(account));
            Account.UnconfirmedTransactionAdded += OnUnconfirmedTransactionAddedEventHandler;

            SymbolsProvider = symbolsProvider ?? throw new ArgumentNullException(nameof(symbolsProvider));
            QuotesProvider = quotesProvider;

            _cts = new CancellationTokenSource();
        }

        public bool IsServiceConnected(TerminalService service)
        {
            return service switch
            {
                TerminalService.Exchange => ExchangeClient.IsConnected,
                TerminalService.MarketData => MarketDataClient.IsConnected,
                TerminalService.All => ExchangeClient.IsConnected && MarketDataClient.IsConnected,
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
                MarketDataRepository = new MarketDataRepository(
                    symbols: SymbolsProvider.GetSymbols(Account.Network));

                // init exchange client
                ExchangeClient = new ExchangeWebClient(configuration, schemes);
                ExchangeClient.Connected += OnExchangeConnectedEventHandler;
                ExchangeClient.Disconnected += OnExchangeDisconnectedEventHandler;
                ExchangeClient.AuthOk += OnExchangeAuthOkEventHandler;
                ExchangeClient.AuthNonce += OnExchangeAuthNonceEventHandler;
                ExchangeClient.Error += OnExchangeErrorEventHandler;
                ExchangeClient.OrderReceived += OnExchangeOrderEventHandler;
                ExchangeClient.SwapReceived += OnSwapReceivedEventHandler;

                // init market data client
                MarketDataClient = new MarketDataWebClient(configuration, schemes);
                MarketDataClient.Connected += OnMarketDataConnectedEventHandler;
                MarketDataClient.Disconnected += OnMarketDataDisconnectedEventHandler;
                MarketDataClient.AuthOk += OnMarketDataAuthOkEventHandler;
                MarketDataClient.AuthNonce += OnMarketDataAuthNonceEventHandler;
                MarketDataClient.Error += OnMarketDataErrorEventHandler;
                MarketDataClient.QuotesReceived += OnQuotesReceivedEventHandler;
                MarketDataClient.EntriesReceived += OnEntriesReceivedEventHandler;
                MarketDataClient.SnapshotReceived += OnSnapshotReceivedEventHandler;

                // start services
                var exchangeConnectTask = ExchangeClient.ConnectAsync();
                var marketDataConnectTask = MarketDataClient.ConnectAsync();
                await Task.WhenAll(exchangeConnectTask, marketDataConnectTask)
                    .ConfigureAwait(false);

                // start async unconfirmed transactions tracking
                _ = TrackUnconfirmedTransactionsAsync(_cts.Token);

                // init swap manager
                SwapManager = new SwapManager(
                    account: Account,
                    swapClient: ExchangeClient,
                    quotesProvider: QuotesProvider,
                    marketDataRepository: MarketDataRepository);

                SwapManager.SwapUpdated += (sender, args) => SwapUpdated?.Invoke(sender, args);

                _ = Task.Run(async () =>
                {
                    // restore swaps
                    await SwapManager
                            .RestoreSwapsAsync(_cts.Token)
                            .ConfigureAwait(false);

                    // timeout control
                    await SwapManager
                            .SwapTimeoutControlAsync(_cts.Token)
                            .ConfigureAwait(false);

                }, _cts.Token);
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

                // cancel all terminal background tasks
                _cts.Cancel();

                // close services
                await Task.WhenAll(ExchangeClient.CloseAsync(), MarketDataClient.CloseAsync())
                    .ConfigureAwait(false);

                ExchangeClient.Connected -= OnExchangeConnectedEventHandler;
                ExchangeClient.Disconnected -= OnExchangeDisconnectedEventHandler;
                ExchangeClient.AuthOk -= OnExchangeAuthOkEventHandler;
                ExchangeClient.AuthNonce -= OnExchangeAuthNonceEventHandler;
                ExchangeClient.Error -= OnExchangeErrorEventHandler;
                ExchangeClient.OrderReceived -= OnExchangeOrderEventHandler;
                ExchangeClient.SwapReceived -= OnSwapReceivedEventHandler;

                MarketDataClient.Connected -= OnMarketDataConnectedEventHandler;
                MarketDataClient.Disconnected -= OnMarketDataDisconnectedEventHandler;
                MarketDataClient.AuthOk -= OnMarketDataAuthOkEventHandler;
                MarketDataClient.AuthNonce -= OnMarketDataAuthNonceEventHandler;
                MarketDataClient.Error -= OnMarketDataErrorEventHandler;
                MarketDataClient.QuotesReceived -= OnQuotesReceivedEventHandler;
                MarketDataClient.EntriesReceived -= OnEntriesReceivedEventHandler;
                MarketDataClient.SnapshotReceived -= OnSnapshotReceivedEventHandler;

                SwapManager.SwapUpdated -= SwapUpdated;
                SwapManager.Clear();
            }
            catch (Exception e)
            {
                Log.Error(e, "StopAsync error.");
            }
        }

        private void SwapUpdatedHandler(object sender, SwapEventArgs swapEventArgs)
        {
            SwapUpdated?.Invoke(sender, swapEventArgs);
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

        public void OrderCancelAsync(Order order) =>
            ExchangeClient.OrderCancelAsync(order);

        public void SubscribeToMarketData(SubscriptionType type) =>
            MarketDataClient.SubscribeAsync(new List<Subscription> { new Subscription { Type = type } });

        public MarketDataOrderBook GetOrderBook(string symbol) =>
            MarketDataRepository?.OrderBookBySymbol(symbol);

        public MarketDataOrderBook GetOrderBook(Symbol symbol) =>
            MarketDataRepository?.OrderBookBySymbol(symbol.Name);

        public Quote GetQuote(Symbol symbol) =>
            MarketDataRepository?.QuoteBySymbol(symbol.Name);

        #region AccountEventHandlers

        private void OnUnconfirmedTransactionAddedEventHandler(object sender, TransactionEventArgs e)
        {
            if (!e.Transaction.IsConfirmed && e.Transaction.State != BlockchainTransactionState.Failed)
                _ = TrackTransactionAsync(e.Transaction, _cts.Token);
        }

        #endregion

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

            ServiceConnected?.Invoke(this, new TerminalServiceEventArgs(TerminalService.Exchange));
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

            ServiceDisconnected?.Invoke(this, new TerminalServiceEventArgs(TerminalService.Exchange));
        }

        private void OnExchangeAuthOkEventHandler(object sender, EventArgs e) =>
            ServiceAuthenticated?.Invoke(this, new TerminalServiceEventArgs(TerminalService.Exchange));

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
            Error?.Invoke(this, new TerminalErrorEventArgs(TerminalService.Exchange, args.Error));
        }

        private async void OnExchangeOrderEventHandler(object sender, OrderEventArgs args)
        {
            // todo: remove warranty outputs if cancel/rejected/partially_filled/filled

            var order = args.Order;

            try
            {
                if (order.Status == OrderStatus.Pending)
                {
                    OnError(TerminalService.Exchange, $"Invalid order status {order.Status}");
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
                    OnError(TerminalService.Exchange, "Error adding order");

                OrderReceived?.Invoke(this, args);
            }
            catch (Exception e)
            {
                OnError(TerminalService.Exchange, e);
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

            ServiceConnected?.Invoke(this, new TerminalServiceEventArgs(TerminalService.MarketData));
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

            ServiceDisconnected?.Invoke(this, new TerminalServiceEventArgs(TerminalService.MarketData));
        }

        private void OnMarketDataAuthOkEventHandler(object sender, EventArgs e) =>
            ServiceAuthenticated?.Invoke(this, new TerminalServiceEventArgs(TerminalService.MarketData));

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

            Error?.Invoke(this, new TerminalErrorEventArgs(TerminalService.Exchange, args.Error));
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
        }

        #endregion

        #region SwapEventHandlers

        private async void OnSwapReceivedEventHandler(object sender, SwapEventArgs args)
        {
            try
            {
                var error = await SwapManager
                    .HandleSwapAsync(args.Swap, _cts.Token)
                    .ConfigureAwait(false);

                if (error != null)
                    OnError(TerminalService.Exchange, error.Description);
            }
            catch (Exception e)
            {
                OnError(TerminalService.Exchange, e);
            }
        }

        #endregion

        private Task BalanceUpdateLoopAsync(CancellationToken cancellationToken)
        {
           return Task.Run(async () =>
           {
               try
               {
                   while (!cancellationToken.IsCancellationRequested)
                   {
                       await new HdWalletScanner(Account)
                           .ScanFreeAddressesAsync(cancellationToken)
                           .ConfigureAwait(false);

                       await Task.Delay(TimeSpan.FromSeconds(Account.UserSettings.BalanceUpdateIntervalInSec), cancellationToken)
                           .ConfigureAwait(false);
                   }
               }
               catch (OperationCanceledException)
               {
                   Log.Debug("Balance autoupdate task canceled.");
               }
               catch (Exception e)
               {
                   Log.Error(e, "Balance autoupdate task error");
               }
           });
        }

        private async Task TrackUnconfirmedTransactionsAsync(
            CancellationToken cancellationToken)
        {
            try
            {
                var txs = await Account
                    .GetTransactionsAsync()
                    .ConfigureAwait(false);

                foreach (var tx in txs)
                    if (!tx.IsConfirmed && tx.State != BlockchainTransactionState.Failed)
                        _ = TrackTransactionAsync(tx, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                Log.Debug("TrackUnconfirmedTransactionsAsync canceled");
            }
            catch (Exception e)
            {
                Log.Error(e, "Unconfirmed transactions track error.");
            }
        }

        private Task TrackTransactionAsync(
            IBlockchainTransaction tx,
            CancellationToken cancellationToken)
        {
            return Task.Run(async () =>
            {
                try
                {
                    while (!cancellationToken.IsCancellationRequested)
                    {
                        var currency = Account.Currencies
                            .GetByName(tx.Currency);

                        var result = await currency
                            .IsTransactionConfirmed(
                                txId: tx.Id,
                                cancellationToken: cancellationToken)
                            .ConfigureAwait(false);

                        if (result.HasError)
                        {
                            await Task.Delay(TransactionConfirmationCheckInterval(tx?.Currency), cancellationToken)
                                .ConfigureAwait(false);

                            continue;
                        }

                        if (result.Value.IsConfirmed || result.Value.Transaction != null && result.Value.Transaction.State == BlockchainTransactionState.Failed)
                        {
                            TransactionProcessedHandler(result.Value.Transaction, cancellationToken);
                            break;
                        }

                        // mark old unconfirmed txs as failed
                        if (tx.CreationTime != null &&
                            DateTime.UtcNow > tx.CreationTime.Value.ToUniversalTime() + DefaultMaxTransactionTimeout &&
                            !Currencies.IsBitcoinBased(tx.Currency))
                        {
                            tx.State = BlockchainTransactionState.Failed;

                            TransactionProcessedHandler(tx, cancellationToken);
                            break;
                        }

                        await Task.Delay(TransactionConfirmationCheckInterval(tx?.Currency), cancellationToken)
                            .ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException)
                {
                    Log.Debug("TrackTransactionAsync canceled.");
                }
                catch (Exception e)
                {
                    Log.Error(e, "TrackTransactionAsync error.");
                }

            }, _cts.Token);
        }

        private async void TransactionProcessedHandler(
            IBlockchainTransaction tx,
            CancellationToken cancellationToken)
        {
            try
            {
                await Account
                    .GetCurrencyAccount<ILegacyCurrencyAccount>(tx.Currency)
                    .UpsertTransactionAsync(tx, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                await Account
                    .UpdateBalanceAsync(tx.Currency, cancellationToken)
                    .ConfigureAwait(false);

                if (Currencies.HasTokens(tx.Currency))
                    await UpdateTokenBalanceAsync(tx, cancellationToken)
                        .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                Log.Debug("Transaction processed handler task canceled.");
            }
            catch (Exception e)
            {
                Log.Error(e, "Error in transaction processed handler.");
            }
        }

        private async Task UpdateTokenBalanceAsync(
            IBlockchainTransaction tx,
            CancellationToken cancellationToken)
        {
            if (tx.Currency == EthereumConfig.Eth)
            {
                // 
            }
            else if (tx.Currency == TezosConfig.Xtz)
            {
                var tezosTx = tx as TezosTransaction;

                if (tezosTx.Params == null)
                    return;

                var tezosAccount = Account
                .GetCurrencyAccount<TezosAccount>(TezosConfig.Xtz);

                var tezosTokensScanner = new TezosTokensScanner(tezosAccount);

                await tezosTokensScanner.ScanAsync(
                    skipUsed: false,
                    cancellationToken: cancellationToken);

                // reload balances for all tezos tokens account
                foreach (var currency in Account.Currencies)
                    if (Currencies.IsTezosToken(currency.Name))
                        Account.GetCurrencyAccount<TezosTokenAccount>(currency.Name)
                            .ReloadBalances();
            }
        }

        private void OnError(TerminalService service, string description)
        {
            Log.Error(description);
            Error?.Invoke(this, new TerminalErrorEventArgs(service, new Error(Errors.InternalError, description)));
        }

        private void OnError(TerminalService service, Exception exception)
        {
            Log.Error(exception, exception.Message);
            Error?.Invoke(this, new TerminalErrorEventArgs(service, new Error(Errors.InternalError, exception.Message)));
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
    }
}
