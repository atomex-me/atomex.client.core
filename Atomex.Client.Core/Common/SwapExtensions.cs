using System.Collections.Generic;
using System.Linq;
using Atomex.Core.Entities;

namespace Atomex.Common
{
    public static class SwapExtensions
    {
        public static Swap ResolveRelationshipsById(
            this Swap swap,
            IList<Symbol> symbols)
        {
            if (swap == null)
                return null;

            swap.Symbol = symbols.FirstOrDefault(s => s.Id == swap.SymbolId);

            return swap;
        }
    }
}