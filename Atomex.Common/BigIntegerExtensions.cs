using System;
using System.Numerics;

namespace Atomex.Common
{
    public static class BigIntegerExtensions
    {
        public const int MaxDecimalPrecision = 28;

        /// <summary>
        /// Convert BigInteger value decimal with a given accuracy
        /// </summary>
        /// <param name="value">BigInteger value</param>
        /// <param name="decimals">Number of zeroes after the decimal point. Can't be greater than 28</param>
        /// <returns>Decimal value</returns>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        /// <exception cref="DivideByZeroException"></exception>
        /// <exception cref="OverflowException"></exception>
        public static decimal ToDecimal(this BigInteger value, int decimals)
        {
            if (decimals > MaxDecimalPrecision)
                throw new ArgumentOutOfRangeException(nameof(decimals), "The number of zeros after the decimal point cannot be more than 28 for type 'Decimal'");

            return ToDecimal(value, decimals, decimals);
        }

        /// <summary>
        /// Convert BigInteger value with a given accuracy to decimal value with a given accuracy
        /// </summary>
        /// <param name="value">BigInteger value</param>
        /// <param name="valueDecimals">Number of zeroes after the decimal point for BigInteger value</param>
        /// <param name="resultDecimals">Number of zeroes after the decimal point for decimal result value. Can't be greater than 28</param>
        /// <returns>Decimal value</returns>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        /// <exception cref="DivideByZeroException"></exception>
        /// <exception cref="OverflowException"></exception>
        public static decimal ToDecimal(this BigInteger value, int valueDecimals, int resultDecimals)
        {
            var divisor = BigInteger.Pow(10, valueDecimals);

            var integerPart = BigInteger.DivRem(value, divisor, out var remainder);

            var result = (decimal)integerPart; // throw overflow if integerPart bigger than decimal type

            if (remainder.IsZero)
                return result;

            resultDecimals = Math.Min(resultDecimals, MaxDecimalPrecision);

            if (valueDecimals > resultDecimals)
            {
                divisor /= BigInteger.Pow(10, valueDecimals - resultDecimals);
                remainder /= BigInteger.Pow(10, valueDecimals - resultDecimals);
            }

            return result + (decimal)remainder / (decimal)divisor;
        }
    }
}