using System;

using Atomex.Blockchain.Bitcoin;
using Atomex.Core;

namespace Atomex.Common.Bson
{
    public static class CurrencyExtensions
    {
        public static Type OutputType(this CurrencyConfig currency)
        {
            if (currency is BitcoinBasedConfig)
                return typeof(BitcoinTxOutput);

            throw new NotSupportedException($"Not supported currency {currency.Name}");
        }
    }
}