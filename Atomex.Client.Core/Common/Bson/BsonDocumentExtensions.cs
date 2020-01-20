using System;
using Atomex.Blockchain.BitcoinBased;
using Atomex.Core;

namespace Atomex.Common.Bson
{
    public static class CurrencyExtensions
    {
        public static Type OutputType(this Currency currency)
        {
            if (currency is BitcoinBasedCurrency)
                return typeof(BitcoinBasedTxOutput);

            throw new NotSupportedException($"Not supported currency {currency.Name}");
        }
    }
}