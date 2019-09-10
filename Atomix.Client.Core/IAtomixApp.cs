using System;
using Atomix.Abstract;
using Atomix.MarketData.Abstract;
using Atomix.Subsystems;
using Atomix.Subsystems.Abstract;
using Atomix.Wallet.Abstract;

namespace Atomix
{
    public interface IAtomixApp
    {
        event EventHandler<AccountChangedEventArgs> AccountChanged;

        IAccount Account { get; }
        ICurrencyQuotesProvider QuotesProvider { get; }
        ICurrencyOrderBookProvider OrderBooksProvider { get; }
        ITerminal Terminal { get; }
        ICurrenciesProvider CurrenciesProvider { get; }
        ISymbolsProvider SymbolsProvider { get; }
        bool HasQuotesProvider { get; }

        IAtomixApp UseCurrenciesProvider(ICurrenciesProvider currenciesProvider);
        IAtomixApp UseSymbolsProvider(ISymbolsProvider symbolsProvider);
        IAtomixApp UseAccount(IAccount account, bool restartTerminal = false);
        IAtomixApp UseQuotesProvider(ICurrencyQuotesProvider quotesProvider);
        IAtomixApp UseOrderBooksProvider(ICurrencyOrderBookProvider orderBooksProvider);
        IAtomixApp UseTerminal(ITerminal terminal);
        IAtomixApp Start();
        IAtomixApp Stop();
    }
}