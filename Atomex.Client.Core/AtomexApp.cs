using System;

using Serilog;

using Atomex.Abstract;
using Atomex.Client.Abstract;
using Atomex.Client.Common;
using Atomex.Client.Entities;
using Atomex.Client.V1.Common;
using Atomex.MarketData.Abstract;
using Atomex.Services;
using Atomex.Services.Abstract;
using Atomex.Swaps;
using Atomex.Swaps.Abstract;
using Atomex.Wallet.Abstract;
using Atomex.MarketData;
using Atomex.MarketData.Common;
using SwapEventArgs = Atomex.Client.V1.Common.SwapEventArgs;
using Swap = Atomex.Core.Swap;
using Order = Atomex.Core.Order;

namespace Atomex
{
    public class AtomexApp : IAtomexApp
    {
        public event EventHandler<AtomexClientChangedEventArgs> AtomexClientChanged;

        public IAtomexClient AtomexClient { get; private set; }
        public IAccount Account { get; private set; }
        public IQuotesProvider QuotesProvider { get; private set; }
        public IOrderBookProvider OrderBooksProvider { get; private set; }
        public ICurrenciesProvider CurrenciesProvider { get; private set; }
        public ISymbolsProvider SymbolsProvider { get; private set; }
        public ICurrenciesUpdater CurrenciesUpdater { get; private set; }
        public ISymbolsUpdater SymbolsUpdater { get; private set; }
        public ISwapManager SwapManager { get; private set; }
        public ITransactionsTracker TransactionsTracker { get; private set; }
        public IMarketDataRepository MarketDataRepository { get; private set; }
        public bool HasQuotesProvider => QuotesProvider != null;

        private IBalanceUpdater _balanceUpdater;
        private bool _storeCanceledOrders;

        public IAtomexApp Start()
        {
            if (AtomexClient != null)
                StartAtomexClient();

            QuotesProvider?.Start();
            OrderBooksProvider?.Start();
            CurrenciesUpdater?.Start();
            SymbolsUpdater?.Start();

            return this;
        }

        public IAtomexApp Stop()
        {
            if (AtomexClient != null)
                StopAtomexClient();

            QuotesProvider?.Stop();
            OrderBooksProvider?.Stop();
            CurrenciesUpdater?.Stop();
            SymbolsUpdater?.Stop();

            return this;
        }

        private async void StartAtomexClient()
        {
            if (Account == null)
                throw new InvalidOperationException("Account not set");

            if (AtomexClient == null)
                throw new InvalidOperationException("AtomexClient not set");

            // start atomex client
            await AtomexClient
                .StartAsync()
                .ConfigureAwait(false);

            // start swap manager
            SwapManager.Start();

            // start transactions tracker
            TransactionsTracker.Start();

            _balanceUpdater.Start();
        }

        private async void StopAtomexClient()
        {
            _balanceUpdater.Stop();

            // stop transactions tracker
            TransactionsTracker.Stop();

            // stop swap manager
            SwapManager.Stop();

            // stop atomex client
            await AtomexClient
                .StopAsync()
                .ConfigureAwait(false);
        }

        public IAtomexApp ChangeAtomexClient(
            IAtomexClient atomexClient,
            IAccount account,
            bool restart = false,
            bool storeCanceledOrders = false)
        {
            if (atomexClient != null && account == null)
                throw new InvalidOperationException("Account must not be null for new atomex client");

            _storeCanceledOrders = storeCanceledOrders;

            var previousAtomexClient = AtomexClient;

            if (previousAtomexClient != null)
            {
                StopAtomexClient();

                // lock account's wallet
                Account?.Lock();

                previousAtomexClient.OrderUpdated    -= AtomexClient_OrderUpdated;
                previousAtomexClient.SwapUpdated     -= AtomexClient_SwapReceived;
                previousAtomexClient.EntriesUpdated  -= AtomexClient_EntriesUpdated;
                previousAtomexClient.QuotesUpdated   -= AtomexClient_QuotesUpdated;
                previousAtomexClient.SnapshotUpdated -= AtomexClient_SnapshotUpdated;
            }

            Account = account;
            AtomexClient = atomexClient;

            if (AtomexClient != null)
            {
                AtomexClient.OrderUpdated    += AtomexClient_OrderUpdated;
                AtomexClient.SwapUpdated     += AtomexClient_SwapReceived;
                AtomexClient.EntriesUpdated  += AtomexClient_EntriesUpdated;
                AtomexClient.QuotesUpdated   += AtomexClient_QuotesUpdated;
                AtomexClient.SnapshotUpdated += AtomexClient_SnapshotUpdated;

                MarketDataRepository = new MarketDataRepository();

                // create swap manager
                SwapManager = new SwapManager(
                    account: Account,
                    swapClient: AtomexClient,
                    quotesProvider: QuotesProvider,
                    marketDataRepository: MarketDataRepository);

                // create transactions tracker
                TransactionsTracker = new TransactionsTracker(Account);

                _balanceUpdater = new BalanceUpdater(
                    account: Account,
                    currenciesProvider: CurrenciesProvider,
                    log: Log.Logger);
            }

            AtomexClientChanged?.Invoke(this, new AtomexClientChangedEventArgs(
                oldClient: previousAtomexClient,
                newClient: AtomexClient));

            if (AtomexClient != null && restart)
                StartAtomexClient();

            return this;
        }

        public IAtomexApp UseCurrenciesProvider(ICurrenciesProvider currenciesProvider)
        {
            CurrenciesProvider = currenciesProvider;
            return this;
        }

        public IAtomexApp UseSymbolsProvider(ISymbolsProvider symbolsProvider)
        {
            SymbolsProvider = symbolsProvider;
            return this;
        }

        public IAtomexApp UseCurrenciesUpdater(ICurrenciesUpdater currenciesUpdater)
        {
            CurrenciesUpdater = currenciesUpdater;
            return this;
        }

        public IAtomexApp UseSymbolsUpdater(ISymbolsUpdater symbolsUpdater)
        {
            SymbolsUpdater = symbolsUpdater;
            return this;
        }

        public IAtomexApp UseQuotesProvider(IQuotesProvider quotesProvider)
        {
            QuotesProvider = quotesProvider;
            return this;
        }
        public IAtomexApp UseOrderBooksProvider(IOrderBookProvider orderBooksProvider)
        {
            OrderBooksProvider = orderBooksProvider;
            return this;
        }

        private async void AtomexClient_OrderUpdated(object sender, OrderEventArgs e)
        {
            try
            {
                // remove canceled orders without trades from local db if StoreCanceledOrders options is true
                if (e.Order.Status == OrderStatus.Canceled && e.Order.LastQty == 0 && !_storeCanceledOrders)
                {
                    await Account
                        .RemoveOrderByIdAsync(e.Order.Id)
                        .ConfigureAwait(false);
                }
                else
                {
                    await Account
                        .UpsertOrderAsync(new Order
                        {
                            Id            = e.Order.Id,
                            ClientOrderId = e.Order.ClientOrderId,
                            Symbol        = e.Order.Symbol,
                            TimeStamp     = e.Order.TimeStamp,
                            Price         = e.Order.Price,
                            Qty           = e.Order.Qty,
                            Side          = e.Order.Side,
                            Type          = e.Order.Type,
                            Status        = e.Order.Status,
                            LastPrice     = e.Order.LastPrice,
                            LeaveQty      = e.Order.LeaveQty,
                            LastQty       = e.Order.LastQty
                        })
                        .ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                // todo: log
            }
        }

        private async void AtomexClient_SwapReceived(object sender, SwapEventArgs e)
        {
            var error = await SwapManager
                .HandleSwapAsync(new Swap
                {
                    Id                   = e.Swap.Id,
                    Status               = e.Swap.Status,
                    TimeStamp            = e.Swap.TimeStamp,
                    OrderId              = e.Swap.OrderId,
                    Symbol               = e.Swap.Symbol,
                    Side                 = e.Swap.Side,
                    Price                = e.Swap.Price,
                    Qty                  = e.Swap.Qty,
                    IsInitiative         = e.Swap.IsInitiative,
                    ToAddress            = e.Swap.ToAddress,
                    RewardForRedeem      = e.Swap.RewardForRedeem,
                    PaymentTxId          = e.Swap.PaymentTxId,
                    RedeemScript         = e.Swap.RedeemScript,
                    RefundAddress        = e.Swap.RefundAddress,
                    PartyAddress         = e.Swap.PartyAddress,
                    PartyRewardForRedeem = e.Swap.PartyRewardForRedeem,
                    PartyPaymentTxId     = e.Swap.PartyPaymentTxId,
                    PartyRedeemScript    = e.Swap.PartyRedeemScript,
                    PartyRefundAddress   = e.Swap.PartyRefundAddress,
                    SecretHash           = e.Swap.SecretHash
                })
                .ConfigureAwait(false);

            if (error != null)
                Log.Error(error.Description);
        }

        private void AtomexClient_SnapshotUpdated(object sender, SnapshotEventArgs e)
        {
            MarketDataRepository.ApplySnapshot(e.Snapshot);
        }

        private void AtomexClient_QuotesUpdated(object sender, QuotesEventArgs e)
        {
            MarketDataRepository.ApplyQuotes(e.Quotes);
        }

        private void AtomexClient_EntriesUpdated(object sender, EntriesEventArgs e)
        {
            MarketDataRepository.ApplyEntries(e.Entries);
        }
    }
}