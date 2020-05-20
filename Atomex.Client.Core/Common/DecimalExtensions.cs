using System;

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
    }
}