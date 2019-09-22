using System.Collections.Generic;
using System.Linq;
using Atomex.Abstract;
using Atomex.Core.Entities;
using Microsoft.Extensions.Configuration;

namespace Atomex
{
    public class Currencies : List<Currency>, ICurrencies
    {
        public Currencies(IConfiguration configuration)
        {
            Add(new Bitcoin(configuration.GetSection("BTC")));
            Add(new Ethereum(configuration.GetSection("ETH")));
            Add(new Litecoin(configuration.GetSection("LTC")));
            Add(new Tezos(configuration.GetSection("XTZ")));
        }

        public void Update(IConfiguration configuration)
        {
            Get<Bitcoin>().Update(configuration.GetSection("BTC"));
            Get<Ethereum>().Update(configuration.GetSection("ETH"));
            Get<Litecoin>().Update(configuration.GetSection("LTC"));
            Get<Tezos>().Update(configuration.GetSection("XTZ"));
        }

        public Currency GetByName(string name)
        {
            return this.FirstOrDefault(c => c.Name == name);
        }

        public T Get<T>() where T : Currency
        {
            return this.FirstOrDefault(c => c.GetType() == typeof(T)) as T;
        }
    }
}