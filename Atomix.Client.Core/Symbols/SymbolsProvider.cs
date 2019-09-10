using System.Collections.Generic;
using Atomix.Abstract;
using Atomix.Core;
using Microsoft.Extensions.Configuration;

namespace Atomix
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

        public SymbolsProvider(
            IConfiguration configuration,
            ICurrenciesProvider currenciesProvider)
        {
            foreach (var network in Networks)
            {
                var networkConfiguration = configuration.GetSection(network.ToString());

                if (networkConfiguration != null)
                    _symbols.Add(network, new Symbols(
                        configuration: networkConfiguration,
                        currencies: currenciesProvider.GetCurrencies(network)));
            }
        }

        public ISymbols GetSymbols(
            Network network)
        {
            return _symbols[network];
        }
    }
}