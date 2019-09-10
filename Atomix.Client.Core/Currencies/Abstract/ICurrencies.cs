using System.Collections.Generic;
using Atomix.Core.Entities;

namespace Atomix.Abstract
{
    public interface ICurrencies : IList<Currency>
    {
        Currency GetByName(string name);
        T Get<T>() where T : Currency;
    }
}