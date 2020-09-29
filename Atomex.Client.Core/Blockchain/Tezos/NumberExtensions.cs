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

        public static decimal ToTokenDigits(this decimal tokens, decimal tokenDigitsMultiplier)
        {
            return Math.Floor(tokens * tokenDigitsMultiplier);
        }

        public static decimal FromTokenDigits(this decimal tokensInDigigts, decimal tokenDigitsMultiplier)
        {
            return tokensInDigigts / tokenDigitsMultiplier;
        }

    }
}