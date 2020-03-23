using System.Collections.Generic;
using System.Linq;
using Atomex.Abstract;
using Atomex.Core;
using Atomex.EthereumTokens;
using Atomex.TezosTokens;
using Microsoft.Extensions.Configuration;

namespace Atomex
{
    public class Currencies : List<Currency>, ICurrencies
    {
        public Currencies(IConfiguration configuration)
        {
            Add(new Bitcoin(configuration.GetSection("BTC")));
            Add(new Ethereum(configuration.GetSection("ETH")));
            Add(new Tether(configuration.GetSection("USDT")));
            Add(new Litecoin(configuration.GetSection("LTC")));
            Add(new Tezos(configuration.GetSection("XTZ")));

            if (configuration.GetSection("FA12").Exists())
                Add(new FA12(configuration.GetSection("FA12")));

            if (configuration.GetSection("TZBTC").Exists())
                Add(new TZBTC(configuration.GetSection("TZBTC")));
        }

        public void Update(IConfiguration configuration)
        {
            Get<Bitcoin>("BTC").Update(configuration.GetSection("BTC"));
            Get<Ethereum>("ETH").Update(configuration.GetSection("ETH"));
            Get<Tether>("USDT").Update(configuration.GetSection("USDT"));
            Get<Litecoin>("LTC").Update(configuration.GetSection("LTC"));
            Get<Tezos>("XTZ").Update(configuration.GetSection("XTZ"));

            if (configuration.GetSection("FA12").Exists())
                Get<FA12>("FA12").Update(configuration.GetSection("FA12"));

            if (configuration.GetSection("TZBTC").Exists())
                Get<TZBTC>("TZBTC").Update(configuration.GetSection("TZBTC"));
        }

        public Currency GetByName(string name) =>
            this.FirstOrDefault(c => c.Name == name);

        public T Get<T>(string name) where T : Currency =>
            GetByName(name) as T;
    }
}