using System.Collections.Generic;
using Atomex.Core;
using Microsoft.Extensions.Configuration;

namespace Atomex.Abstract
{
    public interface ICurrencies : IList<Currency>
    {
        void Update(IConfiguration configuration);
        Currency GetByName(string name);
        T Get<T>() where T : Currency;
    }
}