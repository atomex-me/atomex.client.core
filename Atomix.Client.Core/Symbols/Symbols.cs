using System.Collections.Generic;
using System.Linq;
using Atomix.Abstract;
using Atomix.Core.Entities;
using Microsoft.Extensions.Configuration;

namespace Atomix
{
    public class Symbols : List<Symbol>, ISymbols
    {
        public Symbols(
            IConfiguration configuration,
            ICurrencies currencies)
        {
            var symbols = configuration
                .GetChildren()
                .Select(s => new Symbol(s, currencies));

            AddRange(symbols);
        }

        public Symbol GetByName(
            string name)
        {
            return this.FirstOrDefault(s => s.Name == name);
        }
    }
}