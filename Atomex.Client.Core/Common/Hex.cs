using HexBouncyCastle = Org.BouncyCastle.Utilities.Encoders.Hex;

namespace Atomex.Common
{
    public static class Hex
    {
        public static byte[] FromString(string hex)
        {
            return HexBouncyCastle.Decode(hex);
        }

        public static string ToHexString(this byte[] bytes, int offset, int count)
        {
            return HexBouncyCastle.ToHexString(bytes, offset, count);
        }

        public static string ToHexString(this byte[] bytes)
        {
            return HexBouncyCastle.ToHexString(bytes);

            //var sb = new StringBuilder();

            //foreach (var b in bytes)
            //    sb.Append(b.ToString("x2"));

            //return sb.ToString();
        }
    }
}