using System;
using System.Linq;
using System.Numerics;

using Atomex.Common;
using Atomex.Cryptography.Abstract;

namespace Atomex.Cryptography
{
    /// <summary>
    /// Base58Check Encoding / Decoding (Bitcoin-style)
    /// </summary>
    /// <remarks>
    /// See here for more details: https://en.bitcoin.it/wiki/Base58Check_encoding
    /// </remarks>
    public static class Base58Check
    {
        private const int CheckSumSize = 4;
        private const string Alphabet = "123456789ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz";
        private static readonly sbyte[] AsciiToDigit = {
            0, 1, 2, 3, 4, 5, 6, 7, 8, -1,
            -1, -1, -1, -1, -1, -1, 9, 10, 11, 12,
            13, 14, 15, 16, -1, 17, 18, 19, 20, 21,
            -1, 22, 23, 24, 25, 26, 27, 28, 29, 30,
            31, 32, -1, -1, -1, -1, -1, -1, 33, 34,
            35, 36, 37, 38, 39, 40, 41, 42, 43, -1,
            44, 45, 46, 47, 48, 49, 50, 51, 52, 53,
            54, 55, 56, 57 };

        /// <summary>
        /// Encodes data with a 4-byte checksum
        /// </summary>
        /// <param name="data">Data to be encoded</param>
        /// <returns></returns>
        public static string Encode(ReadOnlySpan<byte> data) =>
            EncodePlain(AddCheckSum(data));

        /// <summary>
        /// Encodes data with a 4-byte checksum and one byte prefix
        /// </summary>
        /// <param name="data">Data to be encoded</param>
        /// <param name="prefix">Prefix byte</param>
        /// <returns></returns>
        public static string Encode(ReadOnlySpan<byte> data, byte prefix) =>
            Encode(data: data, prefix: new[] { prefix });

        /// <summary>
        /// Encodes data with a 4-byte checksum and one prefix
        /// </summary>
        /// <param name="data">Data to be encoded</param>
        /// <param name="prefix">Prefix bytes</param>
        /// <returns></returns>
        public static string Encode(ReadOnlySpan<byte> data, ReadOnlySpan<byte> prefix) =>
            Encode(prefix.Concat(data));

        /// <summary>
        /// Encodes data in plain Base58, without any checksum.
        /// </summary>
        /// <param name="data">The data to be encoded</param>
        /// <returns></returns>
        public static string EncodePlain(ReadOnlySpan<byte> data)
        {
            BigInteger intData = 0;

            foreach (var @byte in data)
                intData = intData * 256 + @byte;

            var result = string.Empty;

            while (intData > 0)
            {
                var remainder = (int)(intData % 58);
                intData /= 58;
                result = Alphabet[remainder] + result;
            }

            // Append `1` for each leading 0 byte
            for (var i = 0; i < data.Length && data[i] == 0; i++)
                result = '1' + result;

            return result;
        }

        /// <summary>
        /// Decodes data in Base58Check format (with 4 byte checksum)
        /// </summary>
        /// <param name="data">Data to be decoded</param>
        /// <returns>Returns decoded data if valid; throws FormatException if invalid</returns>
        public static ReadOnlySpan<byte> Decode(string data)
        {
            var dataWithCheckSum = DecodePlain(data);
            var dataWithoutCheckSum = VerifyAndRemoveCheckSum(dataWithCheckSum);

            if (dataWithoutCheckSum == null)
                throw new FormatException("Base58 checksum is invalid");

            return dataWithoutCheckSum;
        }

        /// <summary>
        /// Decodes encoded string in Base58check format with prefix
        /// </summary>
        /// <param name="encoded">Encoded data</param>
        /// <param name="prefix">Prefix bytes</param>
        /// <returns></returns>
        public static ReadOnlySpan<byte> Decode(string encoded, ReadOnlySpan<byte> prefix)
        {
            var decoded = Decode(encoded);

            if (decoded.Length < prefix.Length)
                return null;

            return decoded[prefix.Length..];
        }

        /// <summary>
        /// Decodes data in plain Base58, without any checksum.
        /// </summary>
        /// <param name="data">Data to be decoded</param>
        /// <returns>Returns decoded data if valid; throws FormatException if invalid</returns>
        public static ReadOnlySpan<byte> DecodePlain(string data)
        {
            BigInteger intData = 0;

            for (var i = 0; i < data.Length; i++)
            {
                var digit = AsciiToDigit[data[i] - 49];

                if (digit < 0)
                    throw new FormatException($"Invalid Base58 character `{data[i]}` at position {i}");

                intData = intData * 58 + digit;
            }

            // encode BigInteger to byte[]
            // leading zero bytes get encoded as leading `1` characters
            var leadingZeroCount = data
                .TakeWhile(c => c == '1')
                .Count();

            var leadingZeros = Enumerable.Repeat((byte) 0, leadingZeroCount);

            var bytesWithoutLeadingZeros = intData
                .ToByteArray()
                .Reverse() // to big endian
                .SkipWhile(b => b == 0); //strip sign byte

            var result = leadingZeros
                .Concat(bytesWithoutLeadingZeros)
                .ToArray();

            return result;
        }

        private static ReadOnlySpan<byte> AddCheckSum(ReadOnlySpan<byte> data) =>
            data.Concat(GetCheckSum(data));

        private static ReadOnlySpan<byte> VerifyAndRemoveCheckSum(ReadOnlySpan<byte> data)
        {
            var result          = data[0..^4];
            var givenCheckSum   = data[^CheckSumSize..];
            var correctCheckSum = GetCheckSum(result);

            return givenCheckSum.SequenceEqual(correctCheckSum)
                ? result
                : null;
        }

        private static ReadOnlySpan<byte> GetCheckSum(ReadOnlySpan<byte> data)
        {
            var hash1 = HashAlgorithm.Sha256.Hash(data);
            var hash2 = HashAlgorithm.Sha256.Hash(hash1);

            return new ReadOnlySpan<byte>(hash2)[..CheckSumSize];
        }
    }
}