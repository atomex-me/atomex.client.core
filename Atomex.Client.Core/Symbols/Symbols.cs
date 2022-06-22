using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

using Microsoft.Extensions.Configuration;
using Serilog;

using Atomex.Abstract;
using Atomex.Core;

namespace Atomex
{
    public class Symbols : ISymbols
    {
        private readonly object _sync = new();
        private IDictionary<string, Symbol> _symbols;

        public Symbols(IConfiguration configuration)
        {
            Update(configuration);
        }

        public void Update(IConfiguration configuration)
        {
            lock (_sync)
            {
                var symbols = new List<Symbol>();

                foreach (var section in configuration.GetChildren())
                {
                    try
                    {
                        symbols.Add(new Symbol(section));
                    }
                    catch (Exception e)
                    {
                        Log.Warning(e, "Symbol configuration update error.");
                    }
                }

                if (_symbols != null)
                {
                    var difference = _symbols.Keys
                        .Except(symbols.Select(s => s.Name))
                        .Select(s => _symbols[s]);

                    if (difference.Any())
                        symbols.AddRange(difference);
                }

                _symbols = symbols
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