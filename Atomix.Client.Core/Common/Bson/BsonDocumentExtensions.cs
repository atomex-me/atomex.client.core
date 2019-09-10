using System;
using Atomix.Blockchain.BitcoinBased;
using Atomix.Core.Entities;

namespace Atomix.Common.Bson
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