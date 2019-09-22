using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Atomex.Api.Proto;
using Atomex.Blockchain;
using Atomex.Blockchain.Abstract;
using Atomex.Common;
using Atomex.Common.Abstract;
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
        //public event EventHandler<MarketDataEventArgs> OrderBookUpdated;
        public event EventHandler<SwapEventArgs> SwapUpdated;

        private ProtoSchemes Schemes { get; set; }
        private ExchangeWebClient ExchangeClient { get; set; }
        private MarketDataWebClient MarketDataClient { get; set; }

        private IConfiguration Configuration { get; }
        private IAccount Account { get; set; }
        private IMarketDataRepository MarketDataRepository { get; set; }
        private ClientSwapManager SwapManager { get; set; }
        private IBackgroundTaskPerformer TaskPerformer { get; }

        public Terminal(
            IConfiguration configuration,
            IAccount account = null)
        {
            Configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            TaskPerformer = new BackgroundTaskPerformer();
            Account = account;

            if (Account != null)
                Account.UnconfirmedTransactionAdded += OnUnconfirmedTransactionAddedEventHandler;
        }

        public async Task ChangeAccountAsync(IAccount account, bool restart = true)
        {
            if (restart)
                await StopAsync()
                    .ConfigureAwait(false);

            Account = account;

            if (Account != null)
                Account.UnconfirmedTransactionAdded += OnUnconfirmedTransactionAddedEventHandler;

            if (restart && Account != null)
                await StartAsync()
                    .ConfigureAwait(false);
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
            Schemes = new ProtoSchemes(
                currencies: Account.Currencies,
                symbols: Account.Symbols);

            // init market data repository
            MarketDataRepository = new MarketDataRepository(Account.Symbols);

            // init exchange client
            ExchangeClient = new ExchangeWebClient(configuration, Schemes);
            ExchangeClient.Connected     += OnExchangeConnectedEventHandler;
            ExchangeClient.Disconnected  += OnExchangeDisconnectedEventHandler;
            ExchangeClient.AuthOk        += OnExchangeAuthOkEventHandler;
            ExchangeClient.AuthNonce     += OnExchangeAuthNonceEventHandler;
            ExchangeClient.Error         += OnExchangeErrorEventHandler;
            ExchangeClient.OrderReceived += OnExchangeOrderEventHandler;
            ExchangeClient.SwapReceived  += OnSwapReceivedEventHandler;

            // init market data client
            MarketDataClient = new MarketDataWebClient(configuration, Schemes);
            MarketDataClient.Connected        += OnMarketDataConnectedEventHandler;
            MarketDataClient.Disconnected     += OnMarketDataDisconnectedEventHandler;
            MarketDataClient.AuthOk           += OnMarketDataAuthOkEventHandler;
            MarketDataClient.AuthNonce        += OnMarketDataAuthNonceEventHandler;
            MarketDataClient.Error            += OnMarketDataErrorEventHandler;
            MarketDataClient.QuotesReceived   += OnQuotesReceivedEventHandler;
            MarketDataClient.EntriesReceived  += OnEntriesReceivedEventHandler;
            MarketDataClient.SnapshotReceived += OnSnapshotReceivedEventHandler;

            // init swap manager
            SwapManager = new ClientSwapManager(
                account: Account,
                swapClient: ExchangeClient,
                taskPerformer: TaskPerformer);
            SwapManager.SwapUpdated += (sender, args) => SwapUpdated?.Invoke(sender, args);

            // run services
            await Task.WhenAll(
                    ExchangeClient.ConnectAsync(),
                    MarketDataClient.ConnectAsync())
                .ConfigureAwait(false);

            // run tracker
            TaskPerformer.EnqueueTask(new BalanceUpdateTask
            {
                Account = Account,
                Interval = TimeSpan.FromSeconds(Account.UserSettings.BalanceUpdateIntervalInSec)
            });
            TaskPerformer.Start();

            // start to track unconfirmed transactions
            await TrackUnconfirmedTransactionsAsync()
                .ConfigureAwait(false);

            // restore swaps
            SwapManager.RestoreSwapsAsync().FireAndForget();
        }

        public async Task StopAsync()
        {
            if (ExchangeClient == null || MarketDataClient == null)
                return;

            Log.Information("Stop terminal services");

            TaskPerformer.Stop();
            TaskPerformer.Clear();

            await Task.WhenAll(
                ExchangeClient.CloseAsync(),
                MarketDataClient.CloseAsync());

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
                TrackUnconfirmedTransaction(e.Transaction);
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

        private void OnSnapshotReceivedEventHandler(
            object sender,
            SnapshotEventArgs args)
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

        private async Task TrackUnconfirmedTransactionsAsync()
        {
            try
            {
                var txs = await Account
                    .GetTransactionsAsync()
                    .ConfigureAwait(false);

                foreach (var tx in txs)
                    if (!tx.IsConfirmed && tx.State != BlockchainTransactionState.Failed)
                        TrackUnconfirmedTransaction(tx);
            }
            catch (Exception e)
            {
                Log.Error(e, "Unconfirmed transactions track error");
            }
        }

        private void TrackUnconfirmedTransaction(IBlockchainTransaction transaction)
        {
            TaskPerformer.EnqueueTask(new TransactionConfirmationCheckTask
            {
                Currency = transaction.Currency,
                Interval = TimeSpan.FromSeconds(30),
                TxId = transaction.Id,
                CompleteHandler = async task =>
                {
                    try
                    {
                        if (!(task is TransactionConfirmationCheckTask confirmationCheckTask))
                            return;

                        await Account
                            .UpsertTransactionAsync(confirmationCheckTask.Tx)
                            .ConfigureAwait(false);

                        await Account
                            .UpdateBalanceAsync(confirmationCheckTask.Currency)
                            .ConfigureAwait(false);
                    }
                    catch (Exception e)
                    {
                        Log.Error(e, "Error in transaction confirmed handler");
                    }
                }
            });
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