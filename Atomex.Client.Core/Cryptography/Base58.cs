using System;
using System.Linq;
using System.Numerics;
using System.Security.Cryptography;
using Atomex.Common;
using WebSocketSharp;

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
        private const string Digits = "123456789ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz";

        /// <summary>
        /// Encodes data with a 4-byte checksum
        /// </summary>
        /// <param name="data">Data to be encoded</param>
        /// <returns></returns>
        public static string Encode(byte[] data)
        {
            return EncodePlain(AddCheckSum(data));
        }

        /// <summary>
        /// Encodes data with a 4-byte checksum and one byte prefix
        /// </summary>
        /// <param name="data">Data to be encoded</param>
        /// <param name="prefix">Prefix byte</param>
        /// <returns></returns>
        public static string Encode(byte[] data, byte prefix)
        {
            return Encode(new[] {prefix}.ConcatArrays(data));
        }

        /// <summary>
        /// Encodes data with a 4-byte checksum and one prefix
        /// </summary>
        /// <param name="payload">Data to be encoded</param>
        /// <param name="prefix">Prefix bytes</param>
        /// <returns></returns>
        public static string Encode(byte[] payload, byte[] prefix)
        {
            return Encode(prefix.ConcatArrays(payload));
        }

        /// <summary>
        /// Encodes data in plain Base58, without any checksum.
        /// </summary>
        /// <param name="data">The data to be encoded</param>
        /// <returns></returns>
        public static string EncodePlain(byte[] data)
        {
            // Decode byte[] to BigInteger
            var intData = data.Aggregate<byte, BigInteger>(0, (current, t) => current * 256 + t);

            // Encode BigInteger to Base58 string
            var result = string.Empty;
            while (intData > 0)
            {
                var remainder = (int)(intData % 58);
                intData /= 58;
                result = Digits[remainder] + result;
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
        public static byte[] Decode(string data)
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
        public static byte[] Decode(string encoded, byte[] prefix)
        {
            int prefixLen = prefix?.Length ?? 0;

            var decoded = Decode(encoded);

            if (decoded.Length < prefixLen)
                return null;

            byte[] result = new byte[decoded.Length - prefixLen];
            Array.Copy(decoded, prefixLen, result, 0, result.Length);

            return result;
        }

        /// <summary>
        /// Decodes data in plain Base58, without any checksum.
        /// </summary>
        /// <param name="data">Data to be decoded</param>
        /// <returns>Returns decoded data if valid; throws FormatException if invalid</returns>
        public static byte[] DecodePlain(string data)
        {
            // Decode Base58 string to BigInteger 
            BigInteger intData = 0;

            for (var i = 0; i < data.Length; i++)
            {
                var digit = Digits.IndexOf(data[i]); //Slow

                if (digit < 0)
                    throw new FormatException($"Invalid Base58 character `{data[i]}` at position {i}");

                intData = intData * 58 + digit;
            }

            // Encode BigInteger to byte[]
            // Leading zero bytes get encoded as leading `1` characters
            var leadingZeroCount = data.TakeWhile(c => c == '1').Count();
            var leadingZeros = Enumerable.Repeat((byte)0, leadingZeroCount);

            var bytesWithoutLeadingZeros = intData
                .ToByteArray()
                .Reverse()// to big endian
                .SkipWhile(b => b == 0);//strip sign byte

            var result = leadingZeros
                .Concat(bytesWithoutLeadingZeros)
                .ToArray();

            return result;
        }

        private static byte[] AddCheckSum(byte[] data)
        {
            var checkSum = GetCheckSum(data);
            var dataWithCheckSum = data.ConcatArrays(checkSum);

            return dataWithCheckSum;
        }

        //Returns null if the checksum is invalid
        private static byte[] VerifyAndRemoveCheckSum(byte[] data)
        {
            var result = ArrayHelpers.SubArray(data, 0, data.Length - CheckSumSize);
            var givenCheckSum = data.SubArray(data.Length - CheckSumSize);
            var correctCheckSum = GetCheckSum(result);

            return givenCheckSum.SequenceEqual(correctCheckSum) ? result : null;
        }

        private static byte[] GetCheckSum(byte[] data)
        {
            using var sha256 = new SHA256Managed();
            var hash1 = sha256.ComputeHash(data);
            var hash2 = sha256.ComputeHash(hash1);

            var result = new byte[CheckSumSize];
            Buffer.BlockCopy(hash2, 0, result, 0, result.Length);

            return result;
        }
    }
}