using System;
using System.Security;

namespace Atomex.Common
{
    public static class ReadOnlySpanExtensions
    {
        private const string EncodingTable = "0123456789ABCDEF";

        public static SecureString ToHexSecureString(this ReadOnlySpan<byte> data)
        {
            var result = new SecureString();

            foreach (var @byte in data)
            {
                result.AppendChar(EncodingTable[(@byte >> 4) & 0x0F]);
                result.AppendChar(EncodingTable[@byte & 0x0F]);
            }

            return result;
        }
    }
}