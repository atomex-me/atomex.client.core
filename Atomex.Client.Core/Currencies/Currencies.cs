using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Atomex.Abstract;
using Atomex.Core;
using Atomex.EthereumTokens;
using Atomex.TezosTokens;
using Microsoft.Extensions.Configuration;

namespace Atomex
{
    public class Currencies : ICurrencies
    {
        private readonly object _sync = new object();
        private IDictionary<string, Currency> _currencies;

        public Currencies(IConfiguration configuration)
        {
            _currencies = configuration
                .GetChildren()
                .Select(GetFromSection).ToDictionary(c => c.Name, c => c);
        }

        public void Update(IConfiguration configuration)
        {
            lock (_sync)
            {
                _currencies = configuration
                    .GetChildren()
                    .Select(GetFromSection).ToDictionary(c => c.Name, c => c);
            }
        }

        public Currency GetByName(string name)
        {
            lock (_sync)
            {
                return _currencies[name];
            }
        }

        public T Get<T>(string name) where T : Currency =>
            GetByName(name) as T;

        public Currency GetFromSection(IConfigurationSection configurationSection)
        {
            return configurationSection.Key switch
            {
                "BTC" => new Bitcoin(configurationSection),
                "LTC" => new Litecoin(configurationSection),
                "ETH" => new Ethereum(configurationSection),
                "XTZ" => new Tezos(configurationSection),
                "USDT" => new Tether(configurationSection),
                "FA12" => new FA12(configurationSection),
                "TZBTC" => new TZBTC(configurationSection),
                _ => throw new NotSupportedException($"{configurationSection.Key} not supported.")
            };
        }

        public IEnumerator<Currency> GetEnumerator()
        {
            lock (_sync)
            {
                return _currencies.Values.GetEnumerator();
            }
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}