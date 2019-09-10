using Atomex.Core;

namespace Atomex.Abstract
{
    public interface ICurrenciesProvider
    {
        ICurrencies GetCurrencies(Network network);
    }
}