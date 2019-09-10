using Atomex.Core;

namespace Atomex.Abstract
{
    public interface ISymbolsProvider
    {
        ISymbols GetSymbols(Network network);
    }
}