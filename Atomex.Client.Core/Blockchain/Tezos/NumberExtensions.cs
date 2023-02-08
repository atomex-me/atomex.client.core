using System.Numerics;

using Atomex.Common;

namespace Atomex.Blockchain.Tezos
{
    public static class NumberExtensions
    {
        public static BigInteger ToTokenDigits(this decimal tokens, BigInteger tokenDigitsMultiplier) =>
            tokens.Multiply(tokenDigitsMultiplier);
    }
}