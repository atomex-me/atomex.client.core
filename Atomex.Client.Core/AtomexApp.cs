using System;
using Atomex.Abstract;
using Atomex.Common;
using Atomex.MarketData.Abstract;
using Atomex.Services;
using Atomex.Services.Abstract;
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
        public ITransactionTracker TransactionTracker { get; private set; }

        public bool HasQuotesProvider => QuotesProvider != null;
        public bool HasOrderBooksProvider => OrderBooksProvider != null;
        public bool HasAtomexClient => AtomexClient != null;

        public IAtomexApp Start()
        {
            if (HasAtomexClient)
                StartAtomexClient();

            if (HasQuotesProvider)
                QuotesProvider.Start();

            if (HasOrderBooksProvider)
                OrderBooksProvider.Start();

            CurrenciesUpdater?.Start();
            SymbolsUpdater?.Start();

            return this;
        }

        public IAtomexApp Stop()
        {
            if (HasAtomexClient)
                StopAtomexClient();

            if (HasQuotesProvider)
                QuotesProvider.Stop();

            if (HasOrderBooksProvider)
                OrderBooksProvider.Stop();

            CurrenciesUpdater?.Stop();
            SymbolsUpdater?.Stop();

            return this;
        }

        private void StartAtomexClient()
        {
            _ = AtomexClient.StartAsync();
        }

        private void StopAtomexClient()
        {
            AtomexClient.Account.Lock();

            _ = AtomexClient.StopAsync();
        }

        public IAtomexApp UseAtomexClient(IAtomexClient atomexClient, bool restart = false)
        {
            if (HasAtomexClient)
                StopAtomexClient();

            AtomexClient = atomexClient;
            AtomexClientChanged?.Invoke(this, new AtomexClientChangedEventArgs(AtomexClient));

            if (HasAtomexClient && restart)
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