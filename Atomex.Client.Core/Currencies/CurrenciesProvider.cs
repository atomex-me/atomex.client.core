using System;
using System.Collections.Generic;
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

                if (networkConfiguration != null)
                    _currencies.Add(network, new Currencies(networkConfiguration));
            }
        }

        public void Update(IConfiguration configuration)
        {
            foreach (var network in Networks)
            {
                var networkConfiguration = configuration.GetSection(network.ToString());

                if (networkConfiguration != null && _currencies.TryGetValue(network, out var currencies))
                    currencies.Update(networkConfiguration); 
                
                if (network == Network.MainNet) {
                    Updated?.Invoke(this, EventArgs.Empty);
                }
            }

            Updated?.Invoke(this, EventArgs.Empty); // this does not execute.
        }

        public ICurrencies GetCurrencies(Network network)
        {
            return _currencies[network];
        }
    }
}