using System;

namespace Atomex.Common
{
    public static class Hex
    {
        public static byte[] FromString(string hex, bool prefixed = false) =>
            Convert.FromHexString(prefixed ? hex[2..] : hex);

        public static string ToHexString(this byte[] bytes, int offset, int count, bool lowerCase = true) => lowerCase
            ? Convert.ToHexString(bytes, offset, count).ToLowerInvariant()
            : Convert.ToHexString(bytes, offset, count);

        public static string ToHexString(this byte[] bytes, bool lowerCase = true) => lowerCase
            ? Convert.ToHexString(bytes, 0, bytes.Length).ToLowerInvariant()
            : Convert.ToHexString(bytes, 0, bytes.Length);

        public static string ToHexString(this ReadOnlySpan<byte> bytes, bool lowerCase = true) => lowerCase
            ? Convert.ToHexString(bytes).ToLowerInvariant()
            : Convert.ToHexString(bytes);
    }
}