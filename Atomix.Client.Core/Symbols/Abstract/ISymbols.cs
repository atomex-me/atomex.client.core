using System.Collections.Generic;
using Atomix.Core.Entities;

namespace Atomix.Abstract
{
    public interface ISymbols : IList<Symbol>
    {
        Symbol GetByName(string name);
    }
}