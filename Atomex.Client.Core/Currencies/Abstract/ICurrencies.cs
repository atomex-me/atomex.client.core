using System.Collections.Generic;

using Microsoft.Extensions.Configuration;

using Atomex.Core;

namespace Atomex.Abstract
{
    public interface ICurrencies : IEnumerable<CurrencyConfig_OLD>
    {
        void Update(IConfiguration configuration);
        CurrencyConfig_OLD GetByName(string name);
        T Get<T>(string name) where T : CurrencyConfig_OLD;
    }
}