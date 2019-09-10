using System;

namespace Atomex.Blockchain.Tezos
{
    public static class NumberExtensions
    {
        public static decimal ToMicroTez(this decimal tz)
        {
            return Math.Round(tz, 6) * 1000000;
        }

        public static decimal ToTez(this decimal mtz)
        {
            return mtz / 1000000;
        }
    }
}