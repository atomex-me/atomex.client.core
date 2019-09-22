using System;
using Atomex.Abstract;
using Atomex.Common;
using Atomex.MarketData.Abstract;
using Atomex.Subsystems;
using Atomex.Subsystems.Abstract;
using Atomex.Wallet.Abstract;

namespace Atomex
{
    public class AtomexApp : IAtomexApp
    {
        public event EventHandler<AccountChangedEventArgs> AccountChanged;

        public IAccount Account { get; private set; }
        public ICurrencyQuotesProvider QuotesProvider { get; private set; }
        public ICurrencyOrderBookProvider OrderBooksProvider { get; private set; }
        public ITerminal Terminal { get; private set; }
        public ICurrenciesProvider CurrenciesProvider { get; private set; }
        public ISymbolsProvider SymbolsProvider { get; private set; }
        public ICurrenciesUpdater CurrenciesUpdater { get; private set; }
        public bool HasAccount => Account != null;
        public bool HasQuotesProvider => QuotesProvider != null;
        public bool HasOrderBooksProvider => OrderBooksProvider != null;
        public bool HasTerminal => Terminal != null;

        public IAtomexApp UseAccount(IAccount account, bool restartTerminal = false)
        {
            var previousAccount = Account;
            Account = account;

            AccountChanged?.Invoke(this, new AccountChangedEventArgs(previousAccount, Account));

            if (HasTerminal)
                Terminal.ChangeAccountAsync(account, restartTerminal).FireAndForget();

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

        public IAtomexApp UseTerminal(ITerminal terminal)
        {
            Terminal = terminal;
            return this;
        }

        public IAtomexApp Start()
        {
            if (HasTerminal && HasAccount) // now client can connect only with authorization by wallet
                Terminal.StartAsync().FireAndForget();

            if (HasQuotesProvider)
                QuotesProvider.Start();
            
            if (HasOrderBooksProvider)
                OrderBooksProvider.Start();

            CurrenciesUpdater?.UpdateAsync().FireAndForget();

            return this;
        }

        public IAtomexApp Stop()
        {
            if (HasTerminal)
                Terminal.StopAsync().FireAndForget();

            if (HasQuotesProvider)
                QuotesProvider.Stop();

            if (HasOrderBooksProvider)
                OrderBooksProvider.Stop();

            return this;
        }
    }
}