using System.Collections.Generic;
using Atomex.Abstract;
using Atomex.Core;
using Microsoft.Extensions.Configuration;

namespace Atomex
{
    public class SymbolsProvider : ISymbolsProvider
    {
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

        public ISymbols GetSymbols(Network network)
        {
            return _symbols[network];
        }
    }
}