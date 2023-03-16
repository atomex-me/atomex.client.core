using System;

using Atomex.Abstract;
using Atomex.Client.Abstract;
using Atomex.Client.Common;
using Atomex.MarketData.Abstract;
using Atomex.Services.Abstract;
using Atomex.Swaps.Abstract;
using Atomex.Wallet.Abstract;

namespace Atomex
{
    public interface IAtomexApp
    {
        event EventHandler<AtomexClientChangedEventArgs> AtomexClientChanged;

        IAtomexClient AtomexClient { get; }
        IAccount Account { get; }
        IQuotesProvider QuotesProvider { get; }
        IOrderBookProvider OrderBooksProvider { get; }
        ICurrenciesProvider CurrenciesProvider { get; }
        ISymbolsProvider SymbolsProvider { get; }
        ICurrenciesUpdater CurrenciesUpdater { get; }
        ISymbolsUpdater SymbolsUpdater { get; }
        ISwapManager SwapManager { get; }
        ITransactionsTracker TransactionsTracker { get; }
        IMarketDataRepository MarketDataRepository { get; }
        ILocalStorage LocalStorage { get; }
        bool HasQuotesProvider { get; }

        IAtomexApp Start();
        IAtomexApp Stop();
        IAtomexApp ChangeAtomexClient(
            IAtomexClient atomexClient,
            IAccount account,
            ILocalStorage localStorage,
            bool restart = false,
            bool storeCanceledOrders = false);
        IAtomexApp UseCurrenciesProvider(ICurrenciesProvider currenciesProvider);
        IAtomexApp UseSymbolsProvider(ISymbolsProvider symbolsProvider);
        IAtomexApp UseCurrenciesUpdater(ICurrenciesUpdater currenciesUpdater);
        IAtomexApp UseSymbolsUpdater(ISymbolsUpdater symbolsUpdater);
        IAtomexApp UseQuotesProvider(IQuotesProvider quotesProvider);
        IAtomexApp UseOrderBooksProvider(IOrderBookProvider orderBooksProvider);
    }
}