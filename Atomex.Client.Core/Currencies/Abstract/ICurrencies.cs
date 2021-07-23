using System.Collections.Generic;

using Microsoft.Extensions.Configuration;

using Atomex.Core;

namespace Atomex.Abstract
{
    public interface ICurrencies : IEnumerable<CurrencyConfig>
    {
        void Update(IConfiguration configuration);
        CurrencyConfig GetByName(string name);
        T Get<T>(string name) where T : CurrencyConfig;
    }
}