using System.Collections.Generic;

using Microsoft.Extensions.Configuration;

using Atomex.Core;

namespace Atomex.Abstract
{
    public interface ICurrencies : IEnumerable<Currency>
    {
        void Update(IConfiguration configuration);
        Currency GetByName(string name);
        T Get<T>(string name) where T : Currency;
    }
}