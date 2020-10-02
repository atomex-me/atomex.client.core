using Microsoft.Extensions.Configuration;
using System;

using Atomex.Core;

namespace Atomex.Abstract
{
    public interface ISymbolsProvider
    {
        event EventHandler Updated;
        void Update(IConfiguration configuration);
        ISymbols GetSymbols(Network network);
    }
}