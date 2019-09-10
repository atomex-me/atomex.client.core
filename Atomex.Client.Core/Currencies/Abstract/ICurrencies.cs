using System.Collections.Generic;
using Atomex.Core.Entities;

namespace Atomex.Abstract
{
    public interface ICurrencies : IList<Currency>
    {
        Currency GetByName(string name);
        T Get<T>() where T : Currency;
    }
}