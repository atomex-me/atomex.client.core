using Atomix.Core;

namespace Atomix.Abstract
{
    public interface ICurrenciesProvider
    {
        ICurrencies GetCurrencies(Network network);
    }
}