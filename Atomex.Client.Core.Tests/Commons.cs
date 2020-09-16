using System;
using System.Linq;
using System.Reflection;
using System.Text;
using Microsoft.Extensions.Configuration;
using NBitcoin;

using Atomex.Abstract;
using Atomex.Common.Configuration;
using Atomex.Core;
using Atomex.Swaps.Abstract;

namespace Atomex.Client.Core.Tests
{
    public static class Commons
    {
        public static Key Alice { get; } = new Key();
        public static Key Bob { get; } = new Key();
        public static byte[] Secret { get; } = Encoding.UTF8.GetBytes("_atomexatomexatomexatomexatomex_");
        public static byte[] SecretHash { get; } = CurrencySwap.CreateSwapSecretHash(Secret);

        private static Assembly CoreAssembly { get; } = AppDomain.CurrentDomain
            .GetAssemblies()
            .FirstOrDefault(a => a.GetName().Name == "Atomex.Client.Core");

        public static readonly IConfiguration CurrenciesConfiguration = new ConfigurationBuilder()
            .AddEmbeddedJsonFile(CoreAssembly, "currencies.json")
            .Build();

        private static readonly IConfiguration SymbolsConfiguration = new ConfigurationBuilder()
            .AddEmbeddedJsonFile(CoreAssembly, "symbols.json")
            .Build();

        public static ICurrencies CurrenciesTestNet
            => new Currencies(CurrenciesConfiguration.GetSection(Atomex.Core.Network.TestNet.ToString()));

        public static ISymbols SymbolsTestNet
            => new Symbols(SymbolsConfiguration.GetSection(Atomex.Core.Network.TestNet.ToString()));

        public static ICurrencies CurrenciesMainNet
            => new Currencies(CurrenciesConfiguration.GetSection(Atomex.Core.Network.MainNet.ToString()));

        public static ISymbols SymbolsMainNet
            => new Symbols(SymbolsConfiguration.GetSection(Atomex.Core.Network.MainNet.ToString()));

        public static Bitcoin BtcMainNet => CurrenciesMainNet.Get<Bitcoin>("BTC");
        public static Litecoin LtcMainNet => CurrenciesMainNet.Get<Litecoin>("LTC");
        //public static Tezos XtzMainNet => CurrenciesMainNet.Get<Tezos>("XTZ");

        public static Bitcoin BtcTestNet => CurrenciesTestNet.Get<Bitcoin>("BTC");
        public static Litecoin LtcTestNet => CurrenciesTestNet.Get<Litecoin>("LTC");
        //public static Tezos XtzTestNet => CurrenciesTestNet.Get<Tezos>("XTZ");
        public static Atomex.Ethereum EthTestNet => CurrenciesTestNet.Get<Atomex.Ethereum>("ETH");

        public static Symbol EthBtcTestNet => SymbolsTestNet.GetByName("ETH/BTC");
        public static Symbol LtcBtcTestNet => SymbolsTestNet.GetByName("LTC/BTC");

        public static string AliceAddress(BitcoinBasedCurrency currency)
        {
            return Alice.PubKey
                .GetAddress(ScriptPubKeyType.Legacy, currency.Network)
                .ToString();
        }

        public static string BobAddress(BitcoinBasedCurrency currency)
        {
            return Bob.PubKey
                .GetAddress(ScriptPubKeyType.Legacy, currency.Network)
                .ToString();
        }

        public static string AliceSegwitAddress(BitcoinBasedCurrency currency)
        {
            return Alice.PubKey.GetSegwitAddress(currency.Network).ToString();
        }

        public static string BobSegwitAddress(BitcoinBasedCurrency currency)
        {
            return Bob.PubKey.GetSegwitAddress(currency.Network).ToString();
        }
    }
}