using System;
using Atomex.Abstract;
using Atomex.MarketData.Abstract;
using Atomex.Services;
using Atomex.Services.Abstract;
using Atomex.Wallet.Abstract;

namespace Atomex
{
    public interface IAtomexApp
    {
        event EventHandler<TerminalChangedEventArgs> TerminalChanged;

        IAtomexClient Terminal { get; }
        IAccount Account { get; }
        ICurrencyQuotesProvider QuotesProvider { get; }
        ICurrencyOrderBookProvider OrderBooksProvider { get; }
        ICurrenciesProvider CurrenciesProvider { get; }
        ISymbolsProvider SymbolsProvider { get; }
        ICurrenciesUpdater CurrenciesUpdater { get; }
        ISymbolsUpdater SymbolsUpdater { get; }
        bool HasQuotesProvider { get; }

        IAtomexApp Start();
        IAtomexApp Stop();
        IAtomexApp UseTerminal(IAtomexClient terminal, bool restart = false);
        IAtomexApp UseCurrenciesProvider(ICurrenciesProvider currenciesProvider);
        IAtomexApp UseSymbolsProvider(ISymbolsProvider symbolsProvider);
        IAtomexApp UseCurrenciesUpdater(ICurrenciesUpdater currenciesUpdater);
        IAtomexApp UseSymbolsUpdater(ISymbolsUpdater symbolsUpdater);
        IAtomexApp UseQuotesProvider(ICurrencyQuotesProvider quotesProvider);
        IAtomexApp UseOrderBooksProvider(ICurrencyOrderBookProvider orderBooksProvider);
    }
}