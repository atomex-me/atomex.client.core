using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Atomex.Api.Proto;
using Atomex.Blockchain;
using Atomex.Blockchain.Abstract;
using Atomex.Blockchain.Helpers;
using Atomex.Common;
using Atomex.Core;
using Atomex.Core.Entities;
using Atomex.MarketData;
using Atomex.MarketData.Abstract;
using Atomex.Subsystems.Abstract;
using Atomex.Swaps;
using Atomex.Wallet;
using Atomex.Wallet.Abstract;
using Atomex.Web;
using Microsoft.Extensions.Configuration;
using Serilog;

namespace Atomex.Subsystems
{
    public class Terminal : ITerminal
    {
        public event EventHandler<TerminalServiceEventArgs> ServiceConnected;
        public event EventHandler<TerminalServiceEventArgs> ServiceDisconnected;
        public event EventHandler<TerminalServiceEventArgs> ServiceAuthenticated;
        public event EventHandler<TerminalErrorEventArgs> Error;
        public event EventHandler<OrderEventArgs> OrderReceived;
        public event EventHandler<MarketDataEventArgs> QuotesUpdated;
        public event EventHandler<SwapEventArgs> SwapUpdated;

        private readonly CancellationTokenSource _cts;
        private ExchangeWebClient ExchangeClient { get; set; }
        private MarketDataWebClient MarketDataClient { get; set; }

        public IAccount Account { get; set; }
        private IConfiguration Configuration { get; }
        private IMarketDataRepository MarketDataRepository { get; set; }
        private ClientSwapManager SwapManager { get; set; }

        private TimeSpan TransactionConfirmationCheckInterval { get; } = TimeSpan.FromSeconds(45);

        public Terminal(IConfiguration configuration, IAccount account)
        {
            Configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));

            Account = account ?? throw new ArgumentNullException(nameof(account));
            Account.UnconfirmedTransactionAdded += OnUnconfirmedTransactionAddedEventHandler;

            _cts = new CancellationTokenSource();
        }

        public bool IsServiceConnected(TerminalService service)
        {
            switch (service)
            {
                case TerminalService.Exchange:
                    return ExchangeClient.IsConnected;
                case TerminalService.MarketData:
                    return MarketDataClient.IsConnected;
                case TerminalService.All:
                    return ExchangeClient.IsConnected &&
                        MarketDataClient.IsConnected;
                default:
                    throw new ArgumentOutOfRangeException(nameof(service), service, null);
            }
        }

        public async Task StartAsync()
        {
            Log.Information("Start terminal services");

            var configuration = Configuration.GetSection($"Services:{Account.Network}");

            // init schemes
            var schemes = new ProtoSchemes(
                currencies: Account.Currencies,
                symbols: Account.Symbols);

            // init market data repository
            MarketDataRepository = new MarketDataRepository(Account.Symbols);

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

            // start async balance update task
            BalanceUpdateLoopAsync(_cts.Token).FireAndForget();

            // start async unconfirmed transactions tracking
            TrackUnconfirmedTransactionsAsync(_cts.Token).FireAndForget();

            // init swap manager
            SwapManager = new ClientSwapManager(
                account: Account,
                swapClient: ExchangeClient);
            SwapManager.SwapUpdated += (sender, args) => SwapUpdated?.Invoke(sender, args);

            // start async swaps restore
            SwapManager.RestoreSwapsAsync(_cts.Token).FireAndForget();
        }

        public async Task StopAsync()
        {
            if (ExchangeClient == null || MarketDataClient == null)
                return;

            Log.Information("Stop terminal services");

            // cancel all terminal background tasks
            _cts.Cancel();

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

        public void OrderCancelAsync(Order order)
        {
            ExchangeClient.OrderCancelAsync(order);
        }

        public void SubscribeToMarketData(SubscriptionType type)
        {
            MarketDataClient.SubscribeAsync(new List<Subscription> {
                new Subscription {Type = type}
            });
        }

        public MarketDataOrderBook GetOrderBook(Symbol symbol)
        {
            return MarketDataRepository?.OrderBookBySymbolId(symbol.Id);
        }

        public Quote GetQuote(Symbol symbol)
        {
            return MarketDataRepository?.QuoteBySymbolId(symbol.Id);
        }

        #region AccountEventHandlers

        private void OnUnconfirmedTransactionAddedEventHandler(object sender, TransactionEventArgs e)
        {
            if (!e.Transaction.IsConfirmed && e.Transaction.State != BlockchainTransactionState.Failed)
                TrackTransactionAsync(e.Transaction, _cts.Token);
        }

        #endregion

        #region ExchangeEventHandlers

        private void OnExchangeConnectedEventHandler(object sender, EventArgs args)
        {
            ServiceConnected?.Invoke(this, new TerminalServiceEventArgs(TerminalService.Exchange));
        }

        private void OnExchangeDisconnectedEventHandler(object sender, EventArgs args)
        {
            ServiceDisconnected?.Invoke(this, new TerminalServiceEventArgs(TerminalService.Exchange));
        }

        private void OnExchangeAuthOkEventHandler(object sender, EventArgs e)
        {
            ServiceAuthenticated?.Invoke(this, new TerminalServiceEventArgs(TerminalService.Exchange));
        }

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
            ServiceConnected?.Invoke(this, new TerminalServiceEventArgs(TerminalService.MarketData));
        }

        private void OnMarketDataDisconnectedEventHandler(object sender, EventArgs args)
        {
            ServiceDisconnected?.Invoke(this, new TerminalServiceEventArgs(TerminalService.MarketData));
        }

        private void OnMarketDataAuthOkEventHandler(object sender, EventArgs e)
        {
            ServiceAuthenticated?.Invoke(this, new TerminalServiceEventArgs(TerminalService.MarketData));
        }

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

            var symbolsIds = new HashSet<int>();
            foreach (var quote in args.Quotes)
            {
                if (!symbolsIds.Contains(quote.SymbolId))
                    symbolsIds.Add(quote.SymbolId);
            }

            foreach (var symbolId in symbolsIds)
            {
                var symbol = Account.Symbols.FirstOrDefault(s => s.Id == symbolId);
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
                await SwapManager
                    .HandleSwapAsync(args.Swap)
                    .ConfigureAwait(false);
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

        private async Task TrackUnconfirmedTransactionsAsync(CancellationToken cancellationToken)
        {
            try
            {
                var txs = await Account
                    .GetTransactionsAsync()
                    .ConfigureAwait(false);

                foreach (var tx in txs)
                    if (!tx.IsConfirmed && tx.State != BlockchainTransactionState.Failed)
                        TrackTransactionAsync(tx, cancellationToken).FireAndForget();
            }
            catch (Exception e)
            {
                Log.Error(e, "Unconfirmed transactions track error");
            }
        }

        private Task TrackTransactionAsync(
            IBlockchainTransaction transaction,
            CancellationToken cancellationToken)
        {
            return Task.Run(async () =>
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    var result = await transaction
                        .IsTransactionConfirmed(
                            cancellationToken: cancellationToken)
                        .ConfigureAwait(false);

                    if (result.HasError) // todo: additional reaction
                        break;

                    if (result.Value.IsConfirmed)
                    {
                        TransactionConfirmedHandler(result.Value.Transaction, cancellationToken);
                        break;
                    }

                    await Task.Delay(TransactionConfirmationCheckInterval, cancellationToken)
                        .ConfigureAwait(false);
                }
            }, _cts.Token);
        }

        private async void TransactionConfirmedHandler(
            IBlockchainTransaction tx,
            CancellationToken cancellationToken)
        {
            try
            {
                await Account
                    .UpsertTransactionAsync(tx, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                await Account
                    .UpdateBalanceAsync(tx.Currency, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                Log.Debug("Transaction confirmation handler task canceled.");
            }
            catch (Exception e)
            {
                Log.Error(e, "Error in transaction confirmed handler");
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
    }
}