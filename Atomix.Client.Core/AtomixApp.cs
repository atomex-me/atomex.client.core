using System;
using Atomix.Abstract;
using Atomix.Common;
using Atomix.MarketData.Abstract;
using Atomix.Subsystems;
using Atomix.Subsystems.Abstract;
using Atomix.Wallet.Abstract;

namespace Atomix
{
    public class AtomixApp : IAtomixApp
    {
        public event EventHandler<AccountChangedEventArgs> AccountChanged;

        public IAccount Account { get; private set; }
        public ICurrencyQuotesProvider QuotesProvider { get; private set; }
        public ICurrencyOrderBookProvider OrderBooksProvider { get; private set; }
        public ITerminal Terminal { get; private set; }
        public ICurrenciesProvider CurrenciesProvider { get; private set; }
        public ISymbolsProvider SymbolsProvider { get; private set; }
        public bool HasAccount => Account != null;
        public bool HasQuotesProvider => QuotesProvider != null;
        public bool HasOrderBooksProvider => OrderBooksProvider != null;
        public bool HasTerminal => Terminal != null;

        public IAtomixApp UseAccount(IAccount account, bool restartTerminal = false)
        {
            var previousAccount = Account;
            Account = account;

            AccountChanged?.Invoke(this, new AccountChangedEventArgs(previousAccount, Account));

            if (HasTerminal)
                Terminal.ChangeAccountAsync(account, restartTerminal).FireAndForget();

            return this;
        }

        public IAtomixApp UseCurrenciesProvider(ICurrenciesProvider currenciesProvider)
        {
            CurrenciesProvider = currenciesProvider;
            return this;
        }

        public IAtomixApp UseSymbolsProvider(ISymbolsProvider symbolsProvider)
        {
            SymbolsProvider = symbolsProvider;
            return this;
        }

        public IAtomixApp UseQuotesProvider(ICurrencyQuotesProvider quotesProvider)
        {
            QuotesProvider = quotesProvider;
            return this;
        }
        public IAtomixApp UseOrderBooksProvider(ICurrencyOrderBookProvider orderBooksProvider)
        {
            OrderBooksProvider = orderBooksProvider;
            return this;
        }

        public IAtomixApp UseTerminal(ITerminal terminal)
        {
            Terminal = terminal;
            return this;
        }

        public IAtomixApp Start()
        {
            if (HasTerminal && HasAccount) // now client can connect only with authorization by wallet
                Terminal.StartAsync().FireAndForget();

            if (HasQuotesProvider)
                QuotesProvider.Start();
            
            if (HasOrderBooksProvider)
                OrderBooksProvider.Start();

            return this;
        }

        public IAtomixApp Stop()
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