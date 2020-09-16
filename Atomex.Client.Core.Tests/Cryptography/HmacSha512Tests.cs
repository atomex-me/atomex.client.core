using System.Collections.Generic;
using System.Text;
using Xunit;

using Atomex.Cryptography;
using Atomex.Cryptography.Abstract;
using Atomex.Common;

namespace Atomex.Client.Core.Tests.Cryptography
{
    public class HmacSha512Tests
    {
        private const int HashSize = 64;

        public static IEnumerable<MacAlgorithm> MacAlgorithms = new List<MacAlgorithm>
        {
            new Atomex.Cryptography.Libsodium.HmacSha512(),
            new Atomex.Cryptography.DotNet.HmacSha512(),
            new HmacSha512()
        };

        public static IEnumerable<(string key, string data, string hash)> Hashes =>
            new List<(string key, string data, string hash)>
            {
                (
                    "",
                    "",
                    "b936cee86c9f87aa5d3c6f2e84cb5a4239a5fe50480a6ec66b70ab5b1f4ac673" +
                    "0c6c515421b327ec1d69402e53dfb49ad7381eb067b338fd7b0cb22247225d47"
                ),
                (
                    "test",
                    "",
                    "01917bf85be0c998598a2332f75c2fe6f662c0900d4391123ca2bc61f073ede3" +
                    "60af8f3afd6e5d3f28dff4b57cc22890aa7b7498cf441f32a6f6e78aca3cafe8"
                ),
                (
                    "",
                    "test",
                    "29c5fab077c009b9e6676b2f082a7ab3b0462b41acf75f075b5a7bac5619ec81" +
                    "c9d8bb2e25b6d33800fba279ee492ac7d05220e829464df3ca8e00298c517764"
                ),
                (
                    "test",
                    "test",
                    "9ba1f63365a6caf66e46348f43cdef956015bea997adeb06e69007ee3ff517df" +
                    "10fc5eb860da3d43b82c2a040c931119d2dfc6d08e253742293a868cc2d82015"
                ),
                (
                    "b936cee86c9f87aa5d3c6f2e84cb5a4239a5fe50480a6ec66b70ab5b1f4ac673" +
                    "0c6c515421b327ec1d69402e53dfb49ad7381eb067b338fd7b0cb22247225d47",
                    "b936cee86c9f87aa5d3c6f2e84cb5a4239a5fe50480a6ec66b70ab5b1f4ac673" +
                    "0c6c515421b327ec1d69402e53dfb49ad7381eb067b338fd7b0cb22247225d47",
                    "a77318eea289123158d50b64e9f58e0c8bce70f47efb3a94617fbbe15d664b11" +
                    "8c8d2d40832aa485e22d24721f03a58bf7bdbbb33ecfe9eb803fb6bd7ab44282"
                ),
                (
                    "cryptography",
                    "atomex",
                    "a771eb9c17eb0ed74b7fb12d5862106025677b55bc9d86771f2981f87c9b8f3d" +
                    "33715ca533ace711ce680bad6d83b58cc7280ae08d4450a6d528bc3ab1f4cc99"
                )
            };

        public static IEnumerable<object[]> Data
        {
            get {
                var data = new List<object[]>();

                foreach (var macAlgorithm in MacAlgorithms)
                    foreach (var hash in Hashes)
                        data.Add(new object[]
                        {
                            macAlgorithm,
                            hash.key,
                            hash.data,
                            hash.hash
                        });

                return data;
            }
        }

        [Theory]
        [MemberData(nameof(Data))]
        public void CanCalculateMac(MacAlgorithm hmacSha512, string key, string data, string hash)
        {
            var keyBytes = Encoding.UTF8.GetBytes(key);
            var dataBytes = Encoding.UTF8.GetBytes(data);
            var hashBytes = hmacSha512.Mac(keyBytes, dataBytes);

            Assert.Equal(Hex.FromString(hash), hashBytes);
        }

        [Theory]
        [MemberData(nameof(Data))]
        public void CanCalculateMacToSpan(MacAlgorithm hmacSha512, string key, string data, string hash)
        {
            var hashBytes = new byte[HashSize];

            var keyBytes = Encoding.UTF8.GetBytes(key);
            var dataBytes = Encoding.UTF8.GetBytes(data);

            hmacSha512.Mac(keyBytes, dataBytes, hashBytes);

            Assert.Equal(Hex.FromString(hash), hashBytes);
        }

        [Theory]
        [MemberData(nameof(Data))]
        public void CanVerify(MacAlgorithm hmacSha512, string key, string data, string hash)
        {
            var keyBytes = Encoding.UTF8.GetBytes(key);
            var dataBytes = Encoding.UTF8.GetBytes(data);

            var result = hmacSha512.Verify(
                keyBytes,
                dataBytes,
                Hex.FromString(hash));

            Assert.True(result);
        }
    }
}