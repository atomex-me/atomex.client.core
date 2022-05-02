using System;

using Atomex.Abstract;
using Atomex.MarketData.Abstract;
using Atomex.Services;
using Atomex.Services.Abstract;
using Atomex.Swaps.Abstract;
using Atomex.Wallet.Abstract;

namespace Atomex
{
    public interface IAtomexApp
    {
        event EventHandler<AtomexClientChangedEventArgs> AtomexClientChanged;

        IAtomexClient_OLD AtomexClient { get; }
        IAccount_OLD Account { get; }
        ICurrencyQuotesProvider QuotesProvider { get; }
        ICurrencyOrderBookProvider OrderBooksProvider { get; }
        ICurrenciesProvider CurrenciesProvider { get; }
        ISymbolsProvider SymbolsProvider { get; }
        ICurrenciesUpdater CurrenciesUpdater { get; }
        ISymbolsUpdater SymbolsUpdater { get; }
        ISwapManager SwapManager { get; }
        ITransactionsTracker TransactionsTracker { get; }
        bool HasQuotesProvider { get; }

        IAtomexApp Start();
        IAtomexApp Stop();
        IAtomexApp UseAtomexClient(IAtomexClient_OLD atomexClient, bool restart = false);
        IAtomexApp UseCurrenciesProvider(ICurrenciesProvider currenciesProvider);
        IAtomexApp UseSymbolsProvider(ISymbolsProvider symbolsProvider);
        IAtomexApp UseCurrenciesUpdater(ICurrenciesUpdater currenciesUpdater);
        IAtomexApp UseSymbolsUpdater(ISymbolsUpdater symbolsUpdater);
        IAtomexApp UseQuotesProvider(ICurrencyQuotesProvider quotesProvider);
        IAtomexApp UseOrderBooksProvider(ICurrencyOrderBookProvider orderBooksProvider);
    }
}