using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

using Microsoft.Extensions.Configuration;
using NBitcoin;

using Atomex.Abstract;
using Atomex.Common.Configuration;
using Atomex.Core;
using Atomex.Swaps.Abstract;
using Atomex.TezosTokens;

namespace Atomex.Client.Core.Tests
{
    public static class Common
    {
        public static Key Alice { get; } = new Key();
        public static Key Bob { get; } = new Key();
        public static byte[] Secret { get; } = Encoding.UTF8.GetBytes("_atomexatomexatomexatomexatomex_");
        public static byte[] SecretHash { get; } = CurrencySwap.CreateSwapSecretHash(Secret);

        private static Assembly CoreAssembly { get; } = AppDomain.CurrentDomain
            .GetAssemblies()
            .FirstOrDefault(a => a.GetName().Name == "Atomex.Client.Core");

        //public static readonly IConfiguration CurrenciesConfiguration = new ConfigurationBuilder()
        //    .AddEmbeddedJsonFile(CoreAssembly, "currencies.json")
        //    .Build();

        public static string CurrenciesConfigurationString
        {
            get
            {
                var resourceName  = "currencies.json";
                var resourceNames = CoreAssembly.GetManifestResourceNames();
                var fullFileName  = resourceNames.FirstOrDefault(n => n.EndsWith(resourceName));
                var stream        = CoreAssembly.GetManifestResourceStream(fullFileName!);
                using StreamReader reader = new(stream!);
                return reader.ReadToEnd();
            }
        }

        public static ICurrenciesProvider CurrencyProvider => new CurrenciesProvider(CurrenciesConfigurationString);

        private static readonly IConfiguration SymbolsConfiguration = new ConfigurationBuilder()
            .AddEmbeddedJsonFile(CoreAssembly, "symbols.json")
            .Build();

        public static ICurrencies CurrenciesTestNet
            => CurrencyProvider.GetCurrencies(Atomex.Core.Network.TestNet); //new Currencies(CurrenciesConfiguration.GetSection(Atomex.Core.Network.TestNet.ToString()));

        public static ISymbols SymbolsTestNet
            => new Symbols(SymbolsConfiguration.GetSection(Atomex.Core.Network.TestNet.ToString()));

        public static ICurrencies CurrenciesMainNet
            => CurrencyProvider.GetCurrencies(Atomex.Core.Network.MainNet); //new Currencies(CurrenciesConfiguration.GetSection(Atomex.Core.Network.MainNet.ToString()));

        public static ISymbols SymbolsMainNet
            => new Symbols(SymbolsConfiguration.GetSection(Atomex.Core.Network.MainNet.ToString()));

        public static BitcoinConfig BtcMainNet => CurrenciesMainNet.Get<BitcoinConfig>("BTC");
        public static LitecoinConfig LtcMainNet => CurrenciesMainNet.Get<LitecoinConfig>("LTC");
        public static TezosConfig XtzMainNet => CurrenciesMainNet.Get<TezosConfig>("XTZ");
        public static TezosConfig TzbtcMainNet => CurrenciesMainNet.Get<Fa12Config>("TZBTC");
        public static BitcoinConfig BtcTestNet => CurrenciesTestNet.Get<BitcoinConfig>("BTC");
        public static LitecoinConfig LtcTestNet => CurrenciesTestNet.Get<LitecoinConfig>("LTC");
        public static TezosConfig XtzTestNet => CurrenciesTestNet.Get<TezosConfig>("XTZ");
        public static EthereumConfig EthTestNet => CurrenciesTestNet.Get<EthereumConfig>("ETH");
        public static Symbol EthBtcTestNet => SymbolsTestNet.GetByName("ETH/BTC");
        public static Symbol LtcBtcTestNet => SymbolsTestNet.GetByName("LTC/BTC");

        public static string AliceAddress(BitcoinBasedConfig currency)
        {
            return Alice.PubKey
                .GetAddress(ScriptPubKeyType.Legacy, currency.Network)
                .ToString();
        }

        public static string BobAddress(BitcoinBasedConfig currency)
        {
            return Bob.PubKey
                .GetAddress(ScriptPubKeyType.Legacy, currency.Network)
                .ToString();
        }

        public static string AliceSegwitAddress(BitcoinBasedConfig currency) =>
            Alice.PubKey.GetSegwitAddress(currency.Network).ToString();

        public static string BobSegwitAddress(BitcoinBasedConfig currency) =>
            Bob.PubKey.GetSegwitAddress(currency.Network).ToString();
    }
}