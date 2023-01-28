using System.Numerics;

using Atomex.Common;

namespace Atomex.Blockchain.Tezos
{
    public static class NumberExtensions
    {
        public static decimal ToTez(this decimal mtz) =>
            mtz / TezosConfig.XtzDigitsMultiplier;

        public static BigInteger ToTokenDigits(this decimal tokens, decimal tokenDigitsMultiplier) =>
            tokens.Multiply(new BigInteger(tokenDigitsMultiplier));

        public static decimal FromTokenDigits(this decimal tokensInDigigts, decimal tokenDigitsMultiplier) =>
            tokensInDigigts / tokenDigitsMultiplier;
    }
}