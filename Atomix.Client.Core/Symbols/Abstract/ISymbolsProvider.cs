using Atomix.Core;

namespace Atomix.Abstract
{
    public interface ISymbolsProvider
    {
        ISymbols GetSymbols(Network network);
    }
}