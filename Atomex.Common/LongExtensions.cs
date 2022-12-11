using System;

namespace Atomex.Common
{
    public static class LongExtensions
    {
        public static decimal ToDecimal(this long value, int decimals) => 
            value / (decimal)Math.Pow(10, decimals);
    }
}
