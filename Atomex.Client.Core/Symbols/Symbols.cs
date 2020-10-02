using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Configuration;

using Atomex.Abstract;
using Atomex.Core;

namespace Atomex
{
    public class Symbols : ISymbols
    {
        private readonly object _sync = new object();
        private IDictionary<string, Symbol> _symbols;

        public Symbols(IConfiguration configuration)
        {
            _symbols = configuration
                .GetChildren()
                .Select(s => new Symbol(s))
                .ToDictionary(s => s.Name, s => s);
        }

        public void Update(IConfiguration configuration)
        {
            lock (_sync)
            {
                _symbols = configuration
                    .GetChildren()
                    .Select(s => new Symbol(s))
                    .ToDictionary(s => s.Name, s => s);
            }
        }

        public Symbol GetByName(string name)
        {
            lock (_sync)
            {
                return _symbols.TryGetValue(name, out var symbol)
                    ? symbol
                    : null;
            }
        }

        public IEnumerator<Symbol> GetEnumerator()
        {
            lock (_sync)
            {
                var result = new List<Symbol>(_symbols.Values);

                return result.GetEnumerator();
            }
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}