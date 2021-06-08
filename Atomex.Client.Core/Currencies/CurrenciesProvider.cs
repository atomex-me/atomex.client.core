using System;
using System.Collections.Generic;
using System.Linq;

using Microsoft.Extensions.Configuration;

using Atomex.Abstract;
using Atomex.Core;

namespace Atomex
{
    public class CurrenciesProvider : ICurrenciesProvider
    {
        public event EventHandler Updated;

        private readonly IDictionary<Network, ICurrencies> _currencies
            = new Dictionary<Network, ICurrencies>();

        private static readonly Network[] Networks =
        {
            Network.MainNet,
            Network.TestNet
        };

        public CurrenciesProvider(IConfiguration configuration)
        {
            foreach (var network in Networks)
            {
                var networkConfiguration = configuration.GetSection(network.ToString());

                if (!networkConfiguration.GetChildren().Any())
                    continue;

                _currencies.Add(network, new Currencies(networkConfiguration));
            }
        }

        public void Update(IConfiguration configuration)
        {
            foreach (var network in Networks)
            {
                var networkConfiguration = configuration.GetSection(network.ToString());

                if (!networkConfiguration.GetChildren().Any())
                    continue;

                if (networkConfiguration != null && _currencies.TryGetValue(network, out var currencies))
                    currencies.Update(networkConfiguration); 
            }

            Updated?.Invoke(this, EventArgs.Empty);
        }

        public ICurrencies GetCurrencies(Network network) => _currencies[network];
    }
}