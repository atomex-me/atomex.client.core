using System;

using Serilog;

using Atomex.Abstract;
using Atomex.Core;
using Atomex.Client.Abstract;
using Atomex.Client.Common;
using Atomex.MarketData.Abstract;
using Atomex.Services;
using Atomex.Services.Abstract;
using Atomex.Swaps;
using Atomex.Swaps.Abstract;
using Atomex.Wallet.Abstract;
using Atomex.MarketData;
using SwapEventArgs = Atomex.Client.V1.Common.SwapEventArgs;

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

        private async void AtomexClient_SwapReceived(object sender, SwapEventArgs e)
        {
            var error = await SwapManager
                .HandleSwapAsync(new Swap
                {
                    Id = e.Swap.Id,
                    Status = e.Swap.Status,
                    TimeStamp = e.Swap.TimeStamp,
                    OrderId = e.Swap.OrderId,
                    Symbol = e.Swap.Symbol,
                    Side = e.Swap.Side,
                    Price = e.Swap.Price,
                    Qty = e.Swap.Qty,
                    IsInitiative = e.Swap.IsInitiative,
                    ToAddress = e.Swap.ToAddress,
                    RewardForRedeem = e.Swap.RewardForRedeem,
                    PaymentTxId = e.Swap.PaymentTxId,
                    RedeemScript = e.Swap.RedeemScript,
                    RefundAddress = e.Swap.RefundAddress,
                    PartyAddress = e.Swap.PartyAddress,
                    PartyRewardForRedeem = e.Swap.PartyRewardForRedeem,
                    PartyPaymentTxId = e.Swap.PartyPaymentTxId,
                    PartyRedeemScript = e.Swap.PartyRedeemScript,
                    PartyRefundAddress = e.Swap.PartyRefundAddress,
                    SecretHash = e.Swap.SecretHash
                })
                .ConfigureAwait(false);

            if (error != null)
                Log.Error(error.Description);
        }

        private async void StopAtomexClient()
        {
            _balanceUpdater.Stop();

            // stop transactions tracker
            TransactionsTracker.Stop();

            // stop swap manager
            SwapManager.Stop();

            // lock account's wallet
            Account.Lock();

            // stop atomex client
            await AtomexClient
                .StopAsync()
                .ConfigureAwait(false);
        }

        public IAtomexApp UseAtomexClient(IAtomexClient atomexClient, bool restart = false)
        {
            var previousAtomexClient = AtomexClient;

            if (previousAtomexClient != null)
            {
                StopAtomexClient();

                previousAtomexClient.SwapUpdated -= AtomexClient_SwapReceived;
            }

            AtomexClient = atomexClient;

            if (AtomexClient != null)
            {
                AtomexClient.SwapUpdated += AtomexClient_SwapReceived;

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
    }
}