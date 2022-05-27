using System.Numerics;

using Atomex.Common;

namespace Atomex.Blockchain.Common
{
    public static class TokensHelper
    {
        public static BigInteger TokensToTokenUnits(
            decimal tokens,
            int decimals) =>
            tokens.Multiply(BigInteger.Pow(10, decimals));

        public static decimal TokenUnitsToTokens(
            BigInteger tokenUnits,
            int decimals) =>
            tokenUnits.ToDecimal(decimals);
    }
}