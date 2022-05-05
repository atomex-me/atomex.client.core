using System;
using System.Numerics;

namespace Atomex.Common
{
    public static class DecimalExtensions
    {
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
    }
}