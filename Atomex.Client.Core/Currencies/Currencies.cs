using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Configuration;

using Atomex.Abstract;
using Atomex.Core;
using Atomex.EthereumTokens;
using Atomex.TezosTokens;

namespace Atomex
{
    public class Currencies : ICurrencies
    {
        private readonly string[] _currenciesOrder = new[]
        {
            "BTC",
            "ETH",
            "LTC",
            "XTZ",
            "USDT",
            "TZBTC",
            "KUSD",
            "NYX",
            "FA2",
            "WBTC",
            "TBTC"
        };

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
                return _currencies.TryGetValue(name, out var currency)
                    ? currency
                    : null;
            }
        }

        public T Get<T>(string name) where T : Currency =>
            GetByName(name) as T;

        private Currency GetFromSection(IConfigurationSection configurationSection)
        {
            return configurationSection.Key switch
            {
                "BTC"   => (Currency) new Bitcoin(configurationSection),
                "LTC"   => new Litecoin(configurationSection),
                "ETH"   => new Ethereum(configurationSection),
                "XTZ"   => new Tezos(configurationSection),
                "USDT"  => new ERC20(configurationSection),
                "TBTC"  => new ERC20(configurationSection),
                "WBTC"  => new ERC20(configurationSection),
                "TZBTC" => new FA12(configurationSection),
                "KUSD"  => new FA12(configurationSection),
                "NYX"   => new NYX(configurationSection),
                "FA2"   => new FA2(configurationSection),
                "FA12"  => new FA12(configurationSection),
                _ => throw new NotSupportedException($"{configurationSection.Key} not supported.")
            };
        }

        public IEnumerator<Currency> GetEnumerator()
        {
            lock (_sync)
            {
                var result = new List<Currency>(_currencies.Values.Count);

                foreach (var currencyByOrder in _currenciesOrder)
                    if (_currencies.TryGetValue(currencyByOrder, out var currency))
                        result.Add(currency);

                return result.GetEnumerator();
                //return _currencies.Values.GetEnumerator();
            }
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public static bool IsBitcoinBased(string name) =>
            name == "BTC" || name == "LTC";
    }
}