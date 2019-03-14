using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Atomix.Blockchain;
using Atomix.Blockchain.Abstract;
using Atomix.Common;
using Atomix.Common.Abstract;
using Atomix.Core;
using Atomix.Core.Entities;
using Atomix.MarketData;
using Atomix.MarketData.Abstract;
using Atomix.Subsystems.Abstract;
using Atomix.Swaps;
using Atomix.Wallet.Abstract;
using Atomix.Web;
using Microsoft.Extensions.Configuration;
using Serilog;

namespace Atomix.Subsystems
{
    public class Terminal : ITerminal
    {
        public event EventHandler<TerminalServiceEventArgs> ServiceConnected;
        public event EventHandler<TerminalServiceEventArgs> ServiceDisconnected;
        public event EventHandler<MarketDataEventArgs> QuotesUpdated;
        //public event EventHandler<MarketDataEventArgs> OrderBookUpdated;
        public event EventHandler<ExecutionReportEventArgs> ExecutionReportReceived;
        public event EventHandler<SwapEventArgs> SwapUpdated;
        public event EventHandler<TerminalErrorEventArgs> Error;

        private ExchangeWebClient ExchangeClient { get; }
        private MarketDataWebClient MarketDataClient { get; }
        private SwapWebClient SwapClient { get; }

        private IAccount Account { get; set; }
        private IMarketDataRepository MarketDataRepository { get; }
        private IBackgroundTaskPerformer TaskPerformer { get; }

        public Terminal(IConfiguration configuration)
            : this(configuration, null)
        {
        }

        public Terminal(IConfiguration configuration, IAccount account)
        {
            ExchangeClient = new ExchangeWebClient(configuration);
            ExchangeClient.Connected               += OnExchangeConnectedEventHandler;
            ExchangeClient.Disconnected            += OnExchangeDisconnectedEventHandler;
            ExchangeClient.AuthOk                  += OnExchangeAuthOkEventHandler;
            ExchangeClient.AuthNonce               += OnExchangeAuthNonceEventHandler;
            ExchangeClient.Error                   += OnExchangeErrorEventHandler;
            ExchangeClient.ExecutionReportReceived += OnExchangeExecutionReportEventHandler;

            MarketDataClient = new MarketDataWebClient(configuration);
            MarketDataClient.Connected        += OnMarketDataConnectedEventHandler;
            MarketDataClient.Disconnected     += OnMarketDataDisconnectedEventHandler;
            MarketDataClient.AuthOk           += OnMarketDataAuthOkEventHandler;
            MarketDataClient.AuthNonce        += OnMarketDataAuthNonceEventHandler;
            MarketDataClient.Error            += OnMarketDataErrorEventHandler;
            MarketDataClient.QuotesReceived   += OnQuotesReceivedEventHandler;
            MarketDataClient.EntriesReceived  += OnEntriesReceivedEventHandler;
            MarketDataClient.SnapshotReceived += OnSnapshotReceivedEventHandler;

            SwapClient = new SwapWebClient(configuration);
            SwapClient.Connected        += OnSwapConnectedEventHandler;
            SwapClient.Disconnected     += OnSwapDisconnectedEventHandler;
            SwapClient.AuthOk           += OnSwapAuthOkEventHandler;
            SwapClient.AuthNonce        += OnSwapAuthNonceEventHandler;
            SwapClient.Error            += OnSwapErrorEventHandler;
            SwapClient.SwapDataReceived += OnSwapDataReceivedEventHandler;

            MarketDataRepository = new MarketDataRepository(Symbols.Available);
            TaskPerformer = new BackgroundTaskPerformer();

            Account = account;

            if (Account != null) {
                Account.UnconfirmedTransactionAdded += OnUnconfirmedTransactionAddedEventHandler;
                Account.SwapsLoaded += OnSwapsLoadedEventHandler;
                Account.LoadSwapsAsync().FireAndForget();
            }
        }

        #region Api

        public async Task<ITerminal> ChangeAccountAsync(IAccount account, bool restart = true)
        {
            if (restart)
                await StopAsync()
                    .ConfigureAwait(false);

            Account = account;

            if (Account != null) {
                Account.UnconfirmedTransactionAdded += OnUnconfirmedTransactionAddedEventHandler;
                Account.SwapsLoaded += OnSwapsLoadedEventHandler;
                Account.LoadSwapsAsync().FireAndForget();
            }

            if (restart && Account != null)
                await StartAsync()
                    .ConfigureAwait(false);

            return this;
        }

        public bool IsServiceConnected(TerminalService service)
        {
            switch (service)
            {
                case TerminalService.Exchange:
                    return ExchangeClient.IsConnected;
                case TerminalService.MarketData:
                    return MarketDataClient.IsConnected;
                case TerminalService.Swap:
                    return SwapClient.IsConnected;
                case TerminalService.All:
                    return ExchangeClient.IsConnected &&
                        MarketDataClient.IsConnected &&
                        SwapClient.IsConnected;
                default:
                    throw new ArgumentOutOfRangeException(nameof(service), service, null);
            }
        }

        public async Task StartAsync()
        {
            Log.Information("Start terminal services");

            // run services
            await Task.WhenAll(
                    ExchangeClient.ConnectAsync(),
                    MarketDataClient.ConnectAsync(),
                    SwapClient.ConnectAsync())
                .ConfigureAwait(false);

            // run tracker
            TaskPerformer.Start();

            // start to track unconfirmed transactions
            await TrackUnconfirmedTransactionsAsync()
                .ConfigureAwait(false);
        }

        public Task StopAsync()
        {
            Log.Information("Stop terminal services");

            TaskPerformer.Stop();

            return Task.WhenAll(
                ExchangeClient.CloseAsync(),
                MarketDataClient.CloseAsync(),
                SwapClient.CloseAsync());
        }

        public async void OrderSendAsync(Order order)
        {
            // todo: mark used outputs as warranty outputs

            order.ClientOrderId = Guid.NewGuid().ToString();

            try
            {
                await Account
                    .AddOrderAsync(order)
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
            return MarketDataRepository.OrderBookBySymbolId(symbol.Id);
        }

        public Quote GetQuote(Symbol symbol)
        {
            return MarketDataRepository.QuoteBySymbolId(symbol.Id);
        }

        #endregion

        #region AccountEventHandlers

        private void OnUnconfirmedTransactionAddedEventHandler(object sender, TransactionEventArgs e)
        {
            TrackUnconfirmedTransaction(e.Transaction);
        }

        private async void OnSwapsLoadedEventHandler(object sender, EventArgs args)
        {
            try
            {
                var swaps = await Account
                    .GetSwapsAsync()
                    .ConfigureAwait(false);

                foreach (var swap in swaps.Cast<SwapState>())
                    await RestoreSwapAsync(swap)
                        .ConfigureAwait(false);
            }
            catch (Exception e)
            {
                Log.Error(e, "Swaps restore error");
            }
        }

        private async Task RestoreSwapAsync(SwapState swapState)
        {
            try
            {
                swapState.Updated += OnSwapUpdated;

                if (!swapState.IsComplete && !swapState.IsCanceled && !swapState.IsRefunded)
                {
                    await new UniversalSwap(
                            swapState: swapState,
                            account: Account,
                            swapClient: SwapClient,
                            taskPerformer: TaskPerformer)
                        .RestoreSwapAsync()
                        .ConfigureAwait(false);
                }
            }
            catch (Exception e)
            {
                Log.Error(e, "Swap {@id} restore error", swapState.Id);
            }
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
            //throw new NotImplementedException();
        }

        private async void OnExchangeAuthNonceEventHandler(object sender, EventArgs args)
        {
            try
            {
                var auth = await Account
                    .CreateAuthRequestAsync(ExchangeClient.Nonce)
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
        }

        private async void OnExchangeExecutionReportEventHandler(object sender, ExecutionReportEventArgs args)
        {
            // todo: remove warranty outputs if cancel/rejected/partially_filled/filled

            var order = args.Report.Order;

            try
            {
                if (order.Status == OrderStatus.Unknown ||
                    order.Status == OrderStatus.Pending)
                {
                    OnError(TerminalService.Exchange, $"Invalid order status {order.Status}");
                    return;
                }

                await HandleOrderAsync(args.Report)
                    .ConfigureAwait(false);

                if (order.Status == OrderStatus.Filled ||
                    order.Status == OrderStatus.PartiallyFilled)
                {
                    await HandleOrderExecutionAsync(args.Report)
                        .ConfigureAwait(false);
                }

                ExecutionReportReceived?.Invoke(this, args);
            }
            catch (Exception e)
            {
                OnError(TerminalService.Exchange, e);
            }
        }

        private async Task HandleOrderExecutionAsync(ExecutionReport report)
        {
            var result = await RunSwapAsync(report)
                .ConfigureAwait(false);

            if (!result)
                OnError(TerminalService.Swap, "Can't run swap");
        }

        private async Task HandleOrderAsync(ExecutionReport report)
        {
            var result = await Account
                .AddOrderAsync(report.Order)
                .ConfigureAwait(false);

            if (!result)
                OnError(TerminalService.Exchange, "Error adding order");
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
            //throw new NotImplementedException();
        }

        private async void OnMarketDataAuthNonceEventHandler(object sender, EventArgs args)
        {
            try
            {
                var auth = await Account
                    .CreateAuthRequestAsync(MarketDataClient.Nonce)
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
            Log.Debug("Market data service error {@Error}", args.Error);
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
                var symbol = Symbols.Available.FirstOrDefault(s => s.Id == symbolId);
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

        private void OnSwapConnectedEventHandler(object sender, EventArgs args)
        {
            ServiceConnected?.Invoke(this, new TerminalServiceEventArgs(TerminalService.Swap));
        }

        private void OnSwapDisconnectedEventHandler(object sender, EventArgs args)
        {
            ServiceDisconnected?.Invoke(this, new TerminalServiceEventArgs(TerminalService.Swap));
        }

        private void OnSwapAuthOkEventHandler(object sender, EventArgs e)
        {
            //throw new NotImplementedException();
        }

        private async void OnSwapAuthNonceEventHandler(object sender, EventArgs args)
        {
            try
            {
                var auth = await Account
                    .CreateAuthRequestAsync(SwapClient.Nonce)
                    .ConfigureAwait(false);

                SwapClient.AuthAsync(auth);
            }
            catch (Exception e)
            {
                Log.Error(e, "Swap auth error");
            }
        }

        private void OnSwapErrorEventHandler(object sender, ErrorEventArgs args)
        {
            Log.Debug("Swap service error {@Error}", args.Error);
        }

        private async void OnSwapDataReceivedEventHandler(object sender, SwapDataEventArgs args)
        {
            var swapData = args.SwapData;

            try
            {
                var swap = (SwapState)await Account
                    .GetSwapByIdAsync(swapData.SwapId)
                    .ConfigureAwait(false);

                if (swap == null) {
                    OnError(TerminalService.Swap, $"Can't find swap with id {swapData.SwapId} in swap repository");
                    return;
                }

                await new UniversalSwap(
                        swapState: swap,
                        account: Account,
                        swapClient: SwapClient,
                        taskPerformer: TaskPerformer)
                    .HandleSwapData(swapData)
                    .ConfigureAwait(false);
            }
            catch (Exception e)
            {
                OnError(TerminalService.Swap, e);
            }
        }

        #endregion

        private async Task<bool> RunSwapAsync(ExecutionReport report)
        {
            Log.Debug("Run swap with id {@swapId}", report.Order.SwapId);

            try
            {
                var swap = new SwapState(
                    order: report.Order,
                    requisites: report.Requisites);

                swap.Updated += OnSwapUpdated;

                var result = await Account
                    .AddSwapAsync(swap)
                    .ConfigureAwait(false);

                if (!result)
                {
                    Log.Error(
                        messageTemplate: "Can't add swap {@swapId} to account swaps repository",
                        propertyValue: swap.Id);

                    return false;
                }

                if (swap.IsInitiator)
                {
                    await new UniversalSwap(
                            swapState: swap,
                            account: Account,
                            swapClient: SwapClient,
                            taskPerformer: TaskPerformer)
                        .InitiateSwapAsync()
                        .ConfigureAwait(false);
                }

                return true;
            }
            catch (Exception e)
            {
                Log.Error(e, "Run swap error");

                return false;
            }
        }

        private async void OnSwapUpdated(object sender, SwapEventArgs args)
        {
            try
            {
                var result = await Account
                    .UpdateSwapAsync(args.Swap)
                    .ConfigureAwait(false);

                if (!result)
                {
                    Log.Error("Swap update error");
                }

                SwapUpdated?.Invoke(this, new SwapEventArgs(args.Swap));
            }
            catch (Exception e)
            {
                Log.Error(e, "Swap update error");
            }
        }

        private async Task TrackUnconfirmedTransactionsAsync()
        {
            try
            {
                var txs = await Account
                    .GetTransactionsAsync()
                    .ConfigureAwait(false);

                foreach (var tx in txs)
                    if (!tx.IsConfirmed())
                        TrackUnconfirmedTransaction(tx);
            }
            catch (Exception e)
            {
                Log.Error(e, "Unconfirmed transactions track error");
            }
        }

        private void TrackUnconfirmedTransaction(IBlockchainTransaction transaction)
        {
            TaskPerformer.EnqueueTask(new TransactionConfirmedTask
            {
                Currency = transaction.Currency,
                Interval = TimeSpan.FromSeconds(30),
                TxId = transaction.Id,
                CompleteHandler = task =>
                {
                    var tx = (task as TransactionConfirmedTask)?.Tx;

                    if (tx != null)
                        Account.AddConfirmedTransactionAsync(tx);
                }
            });
        }

        private void OnError(TerminalService service, string description)
        {
            Log.Error(description);
            Error?.Invoke(this, new TerminalErrorEventArgs(service, description));
        }

        private void OnError(TerminalService service, Exception exception)
        {
            Log.Error(exception, exception.Message);
            Error?.Invoke(this, new TerminalErrorEventArgs(service, exception.Message));
        }
    }
}