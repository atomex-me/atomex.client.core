using System;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;

using Atomex.Abstract;
using Atomex.Core;

namespace Atomex
{
    public class SymbolsProvider : ISymbolsProvider
    {
        public event EventHandler Updated;

        private readonly IDictionary<Network, ISymbols> _symbols
            = new Dictionary<Network, ISymbols>();

        private static readonly Network[] Networks =
        {
            Network.MainNet,
            Network.TestNet
        };

        public SymbolsProvider(IConfiguration configuration)
        {
            foreach (var network in Networks)
            {
                var networkConfiguration = configuration.GetSection(network.ToString());

                if (networkConfiguration != null)
                    _symbols.Add(network, new Symbols(networkConfiguration));
            }
        }

        public void Update(IConfiguration configuration)
        {
            foreach (var network in Networks)
            {
                var networkConfiguration = configuration.GetSection(network.ToString());

                if (networkConfiguration != null && _symbols.TryGetValue(network, out var symbols))
                    symbols.Update(networkConfiguration);
            }

            Updated?.Invoke(this, EventArgs.Empty);
        }

        public ISymbols GetSymbols(Network network)
        {
            return _symbols[network];
        }
    }
}