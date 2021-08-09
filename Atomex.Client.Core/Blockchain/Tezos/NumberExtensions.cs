using System;

namespace Atomex.Blockchain.Tezos
{
    public static class NumberExtensions
    {
        public static decimal ToMicroTez(this decimal tz) =>
            Math.Floor(tz * TezosConfig.XtzDigitsMultiplier);

        public static decimal ToTez(this decimal mtz) =>
            mtz / TezosConfig.XtzDigitsMultiplier;

        public static decimal ToTokenDigits(this decimal tokens, decimal tokenDigitsMultiplier) =>
            Math.Floor(tokens * tokenDigitsMultiplier);

        public static decimal FromTokenDigits(this decimal tokensInDigigts, decimal tokenDigitsMultiplier) =>
            tokensInDigigts / tokenDigitsMultiplier;
    }
}