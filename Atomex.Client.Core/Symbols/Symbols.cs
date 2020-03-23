using System.Collections.Generic;
using System.Linq;
using Atomex.Abstract;
using Atomex.Core;
using Microsoft.Extensions.Configuration;

namespace Atomex
{
    public class Symbols : List<Symbol>, ISymbols
    {
        public Symbols(IConfiguration configuration)
        {
            var symbols = configuration
                .GetChildren()
                .Select(s => new Symbol(s));

            AddRange(symbols);
        }

        public Symbol GetByName(string name)
        {
            return this.FirstOrDefault(s => s.Name == name);
        }
    }
}