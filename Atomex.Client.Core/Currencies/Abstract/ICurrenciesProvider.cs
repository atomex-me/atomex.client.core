using System;
using Microsoft.Extensions.Configuration;

using Atomex.Core;

namespace Atomex.Abstract
{
    public interface ICurrenciesProvider
    {
        event EventHandler Updated;

        void Update(IConfiguration configuration);
        ICurrencies GetCurrencies(Network network);
    }
}