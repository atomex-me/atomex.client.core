using System.Collections.Generic;
using Atomex.Core;
using Microsoft.Extensions.Configuration;

namespace Atomex.Abstract
{
    public interface ICurrencies : IEnumerable<Currency>
    {
        void Update(IConfiguration configuration);
        Currency GetByName(string name);
        T Get<T>(string name) where T : Currency;
    }
}