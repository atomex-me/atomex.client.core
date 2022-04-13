using System;

using Atomex.Abstract;
using Atomex.MarketData.Abstract;
using Atomex.Services;
using Atomex.Services.Abstract;
using Atomex.Swaps;
using Atomex.Swaps.Abstract;
using Atomex.Wallet.Abstract;

namespace Atomex
{
    public class AtomexApp : IAtomexApp
    {
        public event EventHandler<AtomexClientChangedEventArgs> AtomexClientChanged;

        public IAtomexClient AtomexClient { get; private set; }
        public IAccount Account => AtomexClient?.Account;
        public ICurrencyQuotesProvider QuotesProvider { get; private set; }
        public ICurrencyOrderBookProvider OrderBooksProvider { get; private set; }
        public ICurrenciesProvider CurrenciesProvider { get; private set; }
        public ISymbolsProvider SymbolsProvider { get; private set; }
        public ICurrenciesUpdater CurrenciesUpdater { get; private set; }
        public ISymbolsUpdater SymbolsUpdater { get; private set; }
        public ISwapManager SwapManager { get; private set; }
        public ITransactionsTracker TransactionsTracker { get; private set; }

        public bool HasQuotesProvider => QuotesProvider != null;

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
            AtomexClient.SwapReceived += AtomexClient_SwapReceived;

            await AtomexClient
                .StartAsync()
                .ConfigureAwait(false);

            // create and start swap manager
            SwapManager = new SwapManager(
                account: Account,
                swapClient: AtomexClient,
                quotesProvider: QuotesProvider,
                marketDataRepository: AtomexClient.MarketDataRepository);

            SwapManager.Start();

            // create and start transactions tracker
            TransactionsTracker = new TransactionsTracker(Account);
            TransactionsTracker.Start();
        }

        private async void AtomexClient_SwapReceived(object sender, SwapEventArgs e)
        {
            var error = await SwapManager
                .HandleSwapAsync(e.Swap)
                .ConfigureAwait(false);

            if (error != null)
                throw new Exception(error.Description);
        }

        private async void StopAtomexClient()
        {
            // stop transactions tracker
            TransactionsTracker.Stop();

            // stop swap manager
            SwapManager.Stop();

            // lock account's wallet
            AtomexClient.Account.Lock();

            // stop atomex client
            await AtomexClient
                .StopAsync()
                .ConfigureAwait(false);
        }

        public IAtomexApp UseAtomexClient(IAtomexClient atomexClient, bool restart = false)
        {
            if (AtomexClient != null)
                StopAtomexClient();

            AtomexClient = atomexClient;
            AtomexClientChanged?.Invoke(this, new AtomexClientChangedEventArgs(AtomexClient));

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

        public IAtomexApp UseQuotesProvider(ICurrencyQuotesProvider quotesProvider)
        {
            QuotesProvider = quotesProvider;
            return this;
        }
        public IAtomexApp UseOrderBooksProvider(ICurrencyOrderBookProvider orderBooksProvider)
        {
            OrderBooksProvider = orderBooksProvider;
            return this;
        }
    }
}