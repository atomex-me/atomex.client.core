using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

using Microsoft.Extensions.Configuration;
using Serilog;

using Atomex.Abstract;
using Atomex.EthereumTokens;
using Atomex.TezosTokens;
using Atomex.Wallets.Abstract;

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
            "WBTC",
            "TBTC",
            "USDT_XTZ"
        };

        private readonly object _sync = new();
        private IDictionary<string, CurrencyConfig> _currencies;

        public Currencies(IConfiguration configuration)
        {
            Update(configuration);
        }

        public void Update(IConfiguration configuration)
        {
            lock (_sync)
            {
                var currencies = new List<CurrencyConfig>();

                foreach (var section in configuration.GetChildren())
                {
                    try
                    {
                        var currencyConfig = GetFromSection(section);

                        if (currencyConfig != null)
                            currencies.Add(currencyConfig);
                    }
                    catch (Exception e)
                    {
                        Log.Warning(e, "Currency configuration update error.");
                    }
                }

                if (_currencies != null)
                {
                    var difference = _currencies.Keys
                        .Except(currencies.Select(c => c.Name))
                        .Select(c => _currencies[c]);

                    if (difference.Any())
                        currencies.AddRange(difference);
                }

                _currencies = currencies
                    .ToDictionary(c => c.Name, c => c);
            }
        }

        public CurrencyConfig GetByName(string name)
        {
            lock (_sync)
            {
                return _currencies.TryGetValue(name, out var currency)
                    ? currency
                    : null;
            }
        }

        public T Get<T>(string name) where T : CurrencyConfig =>
            GetByName(name) as T;

        private CurrencyConfig GetFromSection(IConfigurationSection configurationSection)
        {
            return configurationSection.Key switch
            {
                "BTC"      => new BitcoinConfig(configurationSection),
                "LTC"      => new LitecoinConfig(configurationSection),
                "ETH"      => new EthereumConfig(configurationSection),
                "XTZ"      => new TezosConfig(configurationSection),
                "USDT"     => new Erc20Config(configurationSection),
                "TBTC"     => new Erc20Config(configurationSection),
                "WBTC"     => new Erc20Config(configurationSection),
                "ERC20"    => new Erc20Config(configurationSection),
                "TZBTC"    => new Fa12Config(configurationSection),
                "KUSD"     => new Fa12Config(configurationSection),
                "USDT_XTZ" => new Fa2Config(configurationSection),
                "FA12"     => new Fa12Config(configurationSection),
                "FA2"      => new Fa2Config(configurationSection),
                _ => null
            };
        }

        public IEnumerator<CurrencyConfig> GetEnumerator()
        {
            
            lock (_sync)
            {
                return _currencies.Values.GetEnumerator();
                //var result = new List<CurrencyConfig>(_currencies.Values.Count);

                //foreach (var currencyByOrder in _currenciesOrder)
                //    if (_currencies.TryGetValue(currencyByOrder, out var currency))
                //        result.Add(currency);

                //return result.GetEnumerator();
            }
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public IEnumerable<CurrencyConfig> GetOrderedPreset()
        {
            lock (_sync)
            {
                var result = new List<CurrencyConfig>(_currencies.Values.Count);

                foreach (var currencyByOrder in _currenciesOrder)
                    if (_currencies.TryGetValue(currencyByOrder, out var currency))
                        result.Add(currency);

                return result;
            }
        }

        public static bool HasTokens(string name) =>
            name == "ETH" ||
            name == "XTZ";

        public static bool IsBitcoinBased(string name) =>
            name == "BTC" ||
            name == "LTC";

        public static bool IsTezosBased(string name) =>
            name == "XTZ" ||
            IsPresetTezosToken(name) ||
            IsTezosTokenStandard(name);

        public static bool IsTezosToken(string name) =>
            IsPresetTezosToken(name) || IsTezosTokenStandard(name);

        public static bool IsPresetTezosToken(string name) =>
            XtzTokens.Contains(name);

        public static bool IsTezosTokenStandard(string name) =>
            XtzTokensStandards.Contains(name);

        public static bool IsEthereumToken(string name) =>
            IsPresetEthereumToken(name) || IsEthereumTokenStandard(name);

        public static bool IsPresetEthereumToken(string name) =>
            EthTokens.Contains(name);

        public static bool IsEthereumTokenStandard(string name) =>
            EthTokensStandards.Contains(name);

        public static bool IsPresetToken(string name) =>
            IsPresetTezosToken(name) || IsPresetEthereumToken(name);

        public static bool IsTokenStandard(string name) =>
            EthTokensStandards.Contains(name) || IsTezosTokenStandard(name);

        public static bool IsToken(string name) =>
            IsTezosToken(name) || IsEthereumToken(name);

        public static string GetBaseChainForPresetToken(string name) =>
            IsPresetTezosToken(name)
                ? "XTZ"
                : IsPresetEthereumToken(name)
                    ? "ETH"
                    : throw new NotSupportedException($"Unsupported preset token {name}");

        public static string[] EthTokens = { "USDT", "WBTC", "TBTC" };
        public static string[] EthTokensStandards = { "ERC20", "ERC721" };

        public static string[] XtzTokens = { "TZBTC", "KUSD", "USDT_XTZ" };
        public static string[] XtzTokensStandards = { "FA12", "FA2" };

    }
}