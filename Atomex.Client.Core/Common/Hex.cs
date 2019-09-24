using HexBouncyCastle = Org.BouncyCastle.Utilities.Encoders.Hex;

namespace Atomex.Common
{
    public static class Hex
    {
        public static byte[] FromString(string hex, bool prefixed = false)
        {
            return HexBouncyCastle.Decode(prefixed ? hex.Substring(2) : hex);
        }

        public static string ToHexString(this byte[] bytes, int offset, int count)
        {
            return HexBouncyCastle.ToHexString(bytes, offset, count);
        }

        public static string ToHexString(this byte[] bytes)
        {
            return HexBouncyCastle.ToHexString(bytes);
        }
    }
}