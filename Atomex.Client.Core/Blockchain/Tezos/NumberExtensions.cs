using Atomex.Common;
using System;

namespace Atomex.Blockchain.Tezos
{
    public static class NumberExtensions
    {
        public static decimal ToMicroTez(this decimal tz)
        {
            return Math.Floor(tz * Atomex.Tezos.XtzDigitsMultiplier);
        }

        public static decimal ToTez(this decimal mtz)
        {
            return mtz / Atomex.Tezos.XtzDigitsMultiplier;
        }
    }
}