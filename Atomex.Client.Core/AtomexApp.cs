﻿using System;
using Atomex.Abstract;
using Atomex.Common;
using Atomex.MarketData.Abstract;
using Atomex.Services;
using Atomex.Services.Abstract;
using Atomex.Wallet.Abstract;

namespace Atomex
{
    public class AtomexApp : IAtomexApp
    {
        public event EventHandler<TerminalChangedEventArgs> TerminalChanged;

        public IAtomexClient Terminal { get; private set; }
        public IAccount Account => Terminal?.Account;
        public ICurrencyQuotesProvider QuotesProvider { get; private set; }
        public ICurrencyOrderBookProvider OrderBooksProvider { get; private set; }
        public ICurrenciesProvider CurrenciesProvider { get; private set; }
        public ISymbolsProvider SymbolsProvider { get; private set; }
        public ICurrenciesUpdater CurrenciesUpdater { get; private set; }
        public ISymbolsUpdater SymbolsUpdater { get; private set; }
        public bool HasQuotesProvider => QuotesProvider != null;
        public bool HasOrderBooksProvider => OrderBooksProvider != null;
        public bool HasTerminal => Terminal != null;

        public IAtomexApp Start()
        {
            if (HasTerminal)
                StartTerminal();

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
            if (HasTerminal)
                StopTerminal();

            if (HasQuotesProvider)
                QuotesProvider.Stop();

            if (HasOrderBooksProvider)
                OrderBooksProvider.Stop();

            CurrenciesUpdater?.Stop();
            SymbolsUpdater?.Stop();

            return this;
        }

        private void StartTerminal()
        {
            _ = Terminal.StartAsync();
        }

        private void StopTerminal()
        {
            Terminal.Account.Lock();

            _ = Terminal.StopAsync();
        }

        public IAtomexApp UseTerminal(IAtomexClient terminal, bool restart = false)
        {
            if (HasTerminal)
                StopTerminal();

            Terminal = terminal;
            TerminalChanged?.Invoke(this, new TerminalChangedEventArgs(Terminal));

            if (HasTerminal && restart)
                StartTerminal();

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