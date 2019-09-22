using Atomex.Core;
using Microsoft.Extensions.Configuration;

namespace Atomex.Abstract
{
    public interface ICurrenciesProvider
    {
        void Update(IConfiguration configuration);
        ICurrencies GetCurrencies(Network network);
    }
}