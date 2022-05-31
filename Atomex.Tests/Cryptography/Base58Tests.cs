using System.Collections.Generic;

using Xunit;

using Atomex.Common;

namespace Atomex.Cryptography
{
    public static class TezosPrefixes
    {
        public static readonly byte[] Tz1 = { 6, 161, 159 };
        public static readonly byte[] Tz2 = { 6, 161, 161 };
        public static readonly byte[] Tz3 = { 6, 161, 164 };
        public static readonly byte[] KT = { 2, 90, 121 };
        public static readonly byte[] Edpk = { 13, 15, 37, 217 };
        public static readonly byte[] Edsk = { 43, 246, 78, 7 };
        public static readonly byte[] Edsig = { 9, 245, 205, 134, 18 };
        public static readonly byte[] b = { 1, 52 };
    }

    public class Base58Tests
    {
        public static IEnumerable<object[]> PlainData = new List<object[]>
        {
            new object[] { "", "" },
            new object[] { "00", "1" },
            new object[] { "0123456789abcdef", "C3CPq7c8PY" },
            new object[] { "00112233445566778899aabbccddeeff", "1UoWww8DGaVGLtea7zU7p" },
        };

        public static IEnumerable<object[]> Data = new List<object[]>
        {
            new object[] { "", "3QJmnh" },
            new object[] { "00", "1Wh4bh" },
            new object[] { "0123456789abcdef", "2FEDkTz84rLWGpux" },
            new object[] { "00112233445566778899aabbccddeeff", "148vjpuxYXixb8DcbaWyeGv2q3u" },
        };

        public static IEnumerable<object[]> DataWithPrefix = new List<object[]>
        {
            new object[] { "", "FaNKMLkXR", TezosPrefixes.Tz1 },
            new object[] { "00", "27LGdMJq5Vz", TezosPrefixes.Tz1 },
            new object[] { "0123456789abcdef", "Bm6zGynfxCgsQigQJgfL", TezosPrefixes.Tz1 },
            new object[] { "00112233445566778899aabbccddeeff", "8wkgGdxZWLBnJKmU8rYgXATNBxx4g5q", TezosPrefixes.Tz1 },
            new object[] { "0000000000000000000000000000000000000000", "tz1Ke2h7sDdakHJQh8WX4Z372du1KChsksyU", TezosPrefixes.Tz1 },
            new object[] { "1111111111111111111111111111111111111111", "tz1MCGdC9qYbSjtWEbup9i17WkohvzwCm2HV", TezosPrefixes.Tz1 },
            new object[] { "00112233445566778899aabbccddeeff00112233", "tz1KePDaisJTUHrqjXbACm8KyutZdVCVAknb", TezosPrefixes.Tz1 },
            new object[] { "", "FaNTPE9cu", TezosPrefixes.Tz2 },
            new object[] { "00", "27LHd3tGko4", TezosPrefixes.Tz2 },
            new object[] { "0123456789abcdef", "Bm79wNMbBZshJNWySuy8", TezosPrefixes.Tz2 },
            new object[] { "00112233445566778899aabbccddeeff", "8wkoQMSU7u7nxdwP2pFhDvGR7tMai8E", TezosPrefixes.Tz2 },
            new object[] { "0000000000000000000000000000000000000000", "tz28KEfLTo3wg2wGyJZMjC1MaDA1q68s6tz5", TezosPrefixes.Tz2 },
            new object[] { "1111111111111111111111111111111111111111", "tz29sUbQkQxxNVXNWmxepLyN4L4iStKf9x8Y", TezosPrefixes.Tz2 },
            new object[] { "00112233445566778899aabbccddeeff00112233", "tz28KbBoKSipQ3Vi1hdzsQ6aXV9a9Nfg7SXp", TezosPrefixes.Tz2 },
            new object[] { "", "FaNrJxnao", TezosPrefixes.Tz3 },
            new object[] { "00", "27LK7k9vWpy", TezosPrefixes.Tz3 },
            new object[] { "0123456789abcdef", "Bm7QRxCy2cew8rLFCTAc", TezosPrefixes.Tz3 },
            new object[] { "00112233445566778899aabbccddeeff", "8wkz6vfL3FWox7CFNkpDH3yzWejZ6We", TezosPrefixes.Tz3 },
            new object[] { "0000000000000000000000000000000000000000", "tz3LL3cfMfBV4fPaPZdcj9TjPa3XbvLiXw9V", TezosPrefixes.Tz3 },
            new object[] { "1111111111111111111111111111111111111111", "tz3MtHYjeH6Vm7yfw32upJRjsgxEDiVdgA85", TezosPrefixes.Tz3 },
            new object[] { "00112233445566778899aabbccddeeff00112233", "tz3LLQ98DJrMnfx1RxiFsMYxLr35vCkimTmY", TezosPrefixes.Tz3 },
            new object[] { "", "6B1tmbdxF", TezosPrefixes.KT },
            new object[] { "00", "PqCXkN2Tdo", TezosPrefixes.KT },
            new object[] { "0123456789abcdef", "4pVPTwX8fdHAVfJDPkWc", TezosPrefixes.KT },
            new object[] { "00112233445566778899aabbccddeeff", "3pX2agoiUXBFhTfXxWDPik6xiFau5de", TezosPrefixes.KT },
            new object[] { "0000000000000000000000000000000000000000", "KT18amZmM5W7qDWVt2pH6uj7sCEd3kbzLrHT", TezosPrefixes.KT },
            new object[] { "1111111111111111111111111111111111111111", "KT1A91VqdhR8Xg6bRWDaC4h8MK9KfYo9o4Vi", TezosPrefixes.KT },
            new object[] { "00112233445566778899aabbccddeeff00112233", "KT18b86ECjAzZE4vvRtvF7pLpUEBN38uS9aP", TezosPrefixes.KT },
        };

        [Theory]
        [MemberData(nameof(PlainData))]
        public void CanEncodePlain(string hex, string base58)
        {
            Assert.Equal(base58, Base58Check.EncodePlain(Hex.FromString(hex)));
        }

        [Theory]
        [MemberData(nameof(PlainData))]
        public void CanDecodePlain(string hex, string base58)
        {
            Assert.Equal(hex, Base58Check.DecodePlain(base58).ToArray().ToHexString());
        }

        [Theory]
        [MemberData(nameof(Data))]
        public void CanEncode(string hex, string base58)
        {
            Assert.Equal(base58, Base58Check.Encode(Hex.FromString(hex)));
        }

        [Theory]
        [MemberData(nameof(Data))]
        public void CanDecode(string hex, string base58)
        {
            Assert.Equal(hex, Base58Check.Decode(base58).ToArray().ToHexString());
        }

        [Theory]
        [MemberData(nameof(DataWithPrefix))]
        public void CanEncodeWithPrefix(string hex, string base58, byte[] prefix)
        {
            Assert.Equal(base58, Base58Check.Encode(Hex.FromString(hex), prefix));
        }

        [Theory]
        [MemberData(nameof(DataWithPrefix))]
        public void CanDecodeWithPrefix(string hex, string base58, byte[] prefix)
        {
            Assert.Equal(hex, Base58Check.Decode(base58, prefix).ToArray().ToHexString());
        }
    }
}