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
        event EventHandler<TerminalChangedEventArgs> TerminalChanged;

        ITerminal Terminal { get; }
        IAccount Account { get; }
        ICurrencyQuotesProvider QuotesProvider { get; }
        ICurrencyOrderBookProvider OrderBooksProvider { get; }
        ICurrenciesProvider CurrenciesProvider { get; }
        ISymbolsProvider SymbolsProvider { get; }
        bool HasQuotesProvider { get; }

        IAtomexApp Start();
        IAtomexApp Stop();
        IAtomexApp UseTerminal(ITerminal terminal, bool restart = false);
        IAtomexApp UseCurrenciesProvider(ICurrenciesProvider currenciesProvider);
        IAtomexApp UseSymbolsProvider(ISymbolsProvider symbolsProvider);
        IAtomexApp UseCurrenciesUpdater(ICurrenciesUpdater currenciesUpdater);
        IAtomexApp UseQuotesProvider(ICurrencyQuotesProvider quotesProvider);
        IAtomexApp UseOrderBooksProvider(ICurrencyOrderBookProvider orderBooksProvider);
    }
}