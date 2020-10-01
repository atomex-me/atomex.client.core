using System.Collections.Generic;
using Microsoft.Extensions.Configuration;

using Atomex.Core;

namespace Atomex.Abstract
{
    public interface ISymbols : IEnumerable<Symbol>
    {
        void Update(IConfiguration configuration);
        Symbol GetByName(string name);
    }
}