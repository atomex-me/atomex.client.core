using System.Collections.Generic;
using Atomix.Abstract;
using Atomix.Core;
using Microsoft.Extensions.Configuration;

namespace Atomix
{
    public class CurrenciesProvider : ICurrenciesProvider
    {
        private readonly IDictionary<Network, ICurrencies> _currencies
            = new Dictionary<Network, ICurrencies>();

        private static readonly Network[] Networks =
        {
            Network.MainNet,
            Network.TestNet
        };

        public CurrenciesProvider(
            IConfiguration configuration)
        {
            foreach (var network in Networks)
            {
                var networkConfiguration = configuration.GetSection(network.ToString());

                if (networkConfiguration != null)
                    _currencies.Add(network, new Currencies(networkConfiguration));
            }
        }

        public ICurrencies GetCurrencies(
            Network network)
        {
            return _currencies[network];
        }
    }
}