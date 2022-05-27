using System.Numerics;

namespace Atomex.Common
{
    public static class BigIntegerExtensions
    {
        public const int MaxDecimalPrecision = 28;

        public static decimal ToDecimal(this BigInteger bigInteger, int decimals)
        {
            var divisor = BigInteger.Pow(10, decimals);

            var integerPart = BigInteger.DivRem(bigInteger, divisor, out var remainder);

            var result = (decimal)integerPart; // throw overflow if integerPart bigger than decimal type

            if (remainder.IsZero)
                return result;

            if (decimals > MaxDecimalPrecision)
            {
                divisor /= BigInteger.Pow(10, decimals - MaxDecimalPrecision);
                remainder /= BigInteger.Pow(10, decimals - MaxDecimalPrecision); 
            }

            return result + (decimal)remainder / (decimal)divisor;
        }
    }
}