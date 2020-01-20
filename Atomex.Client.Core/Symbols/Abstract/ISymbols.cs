using System.Collections.Generic;
using Atomex.Core;

namespace Atomex.Abstract
{
    public interface ISymbols : IList<Symbol>
    {
        Symbol GetByName(string name);
    }
}