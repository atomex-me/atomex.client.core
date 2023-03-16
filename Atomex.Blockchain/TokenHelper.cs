using System.Numerics;

using Atomex.Common;

namespace Atomex.Blockchain
{
    public static class TokenHelper
    {
        public static BigInteger ToTokens(this decimal value, int decimals) =>
            value.ToBigInteger(decimals);

        public static decimal FromTokens(this BigInteger tokens, int decimals) =>
            tokens.ToDecimal(decimals);
    }
}