using System;
using Atomex.Abstract;
using Atomex.MarketData.Abstract;
using Atomex.Subsystems;
using Atomex.Subsystems.Abstract;
using Atomex.Wallet.Abstract;

namespace Atomex
{
    public interface IAtomexApp
    {
        event EventHandler<AccountChangedEventArgs> AccountChanged;

        IAccount Account { get; }
        ICurrencyQuotesProvider QuotesProvider { get; }
        ICurrencyOrderBookProvider OrderBooksProvider { get; }
        ITerminal Terminal { get; }
        ICurrenciesProvider CurrenciesProvider { get; }
        ISymbolsProvider SymbolsProvider { get; }
        bool HasQuotesProvider { get; }

        IAtomexApp UseCurrenciesProvider(ICurrenciesProvider currenciesProvider);
        IAtomexApp UseSymbolsProvider(ISymbolsProvider symbolsProvider);
        IAtomexApp UseAccount(IAccount account, bool restartTerminal = false);
        IAtomexApp UseQuotesProvider(ICurrencyQuotesProvider quotesProvider);
        IAtomexApp UseOrderBooksProvider(ICurrencyOrderBookProvider orderBooksProvider);
        IAtomexApp UseTerminal(ITerminal terminal);
        IAtomexApp Start();
        IAtomexApp Stop();
    }
}