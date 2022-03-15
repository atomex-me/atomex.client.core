using System;
using System.Globalization;
using System.Numerics;

namespace Atomex.Common
{
    public static class DecimalExtensions
    {
        public static decimal RoundToSignificantDigits(this decimal d, int digits)
        {
            if (d == 0)
                return 0;

            double scale = Math.Pow(10, Math.Floor(Math.Log10(Math.Abs((double)d))) + 1);
            return (decimal)(scale * Math.Round((double)d / scale, digits));
        }

        public static bool TryParseWithRound(this string s, int decimals, out decimal result)
        {
            const int MaxDecimals = 28;

            result = 0;

            if (s == null)
                return false;

            if (decimals <= MaxDecimals && decimal.TryParse(s, out var decimalDividend))
            {
                try
                {
                    var bigIntegerDivisor = BigInteger.Pow(10, decimals);

                    var decimalDivisor = (decimal)bigIntegerDivisor;

                    result = decimalDividend / decimalDivisor;
                    return true;
                }
                catch
                {
                    // argument out of range
                    // division by zero
                    // overflow
                }
            }

            try
            {
                var isNegative = s.StartsWith("-");
                var unsignedS = s.TrimStart('-');

                var dividendDecimals = unsignedS.Length;

                var decimalsToRemove = Math.Max(Math.Max(dividendDecimals - MaxDecimals, decimals - MaxDecimals), 0);

                var startIndexToRemove = Math.Max(unsignedS.Length - decimalsToRemove, 0);
                var lengthToRemove = Math.Min(unsignedS.Length, decimalsToRemove);

                if (lengthToRemove > 0)
                    unsignedS = unsignedS.Remove(startIndexToRemove, lengthToRemove);

                if (unsignedS.Length == 0)
                {
                    result = 0;
                    return true;
                }

                if (!decimal.TryParse(unsignedS, out result))
                    return false;

                var bigIntegerDivisor = BigInteger.Pow(10, decimals - decimalsToRemove);

                var decimalDivisor = (decimal)bigIntegerDivisor;

                result /= decimalDivisor;

                if (isNegative)
                    result = -result;

                return true;
            }
            catch
            {
                // argument out of range
                // division by zero
                return false;
            }
        }

        public static string GetFormatWithPrecision(int precision) =>
            "0." + new string('#', precision);

        public static string FormatWithPrecision(this decimal d, int precision) =>
            d.ToString(GetFormatWithPrecision(precision), CultureInfo.CurrentCulture);
    }
}