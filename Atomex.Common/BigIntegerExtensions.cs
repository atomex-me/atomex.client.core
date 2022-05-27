using System;
using System.Numerics;

namespace Atomex.Common
{
    public static class BigIntegerExtensions
    {
        public const int MaxDecimalPrecision = 28;

        public static decimal ToDecimal(this BigInteger bigInteger, int decimals, int targetDecimals)
        {
            var divisor = BigInteger.Pow(10, decimals);

            var integerPart = BigInteger.DivRem(bigInteger, divisor, out var remainder);

            var result = (decimal)integerPart; // throw overflow if integerPart bigger than decimal type

            if (remainder.IsZero)
                return result;

            targetDecimals = Math.Min(targetDecimals, MaxDecimalPrecision);

            if (decimals > targetDecimals)
            {
                divisor /= BigInteger.Pow(10, decimals - targetDecimals);
                remainder /= BigInteger.Pow(10, decimals - targetDecimals);
            }

            return result + (decimal)remainder / (decimal)divisor;
        }
    }
}