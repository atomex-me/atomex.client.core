using System.Collections.Generic;
using System.Text;
using Xunit;

using Atomex.Common;
using Atomex.Cryptography.Abstract;

namespace Atomex.Cryptography
{
    public class HmacSha256Tests
    {
        private static readonly IEnumerable<MacAlgorithm> MacAlgorithms = new List<MacAlgorithm>
        {
            new Libsodium.HmacSha256(),
            new DotNet.HmacSha256(),
            new HmacSha256()
        };

        public static IEnumerable<(string key, string data, string hash)> Hashes =>
            new List<(string key, string data, string hash)>
            {
                (
                    "",
                    "",
                    "b613679a0814d9ec772f95d778c35fc5ff1697c493715653c6c712144292c5ad"
                ),
                (
                    "test",
                    "",
                    "ad71148c79f21ab9eec51ea5c7dd2b668792f7c0d3534ae66b22f71c61523fb3"
                ),
                (
                    "",
                    "test",
                    "43b0cef99265f9e34c10ea9d3501926d27b39f57c6d674561d8ba236e7a819fb"
                ),
                (
                    "test",
                    "test",
                    "88cd2108b5347d973cf39cdf9053d7dd42704876d8c9a9bd8e2d168259d3ddf7"
                ),
                (
                    "b936cee86c9f87aa5d3c6f2e84cb5a4239a5fe50480a6ec66b70ab5b1f4ac673" +
                    "0c6c515421b327ec1d69402e53dfb49ad7381eb067b338fd7b0cb22247225d47",
                    "b936cee86c9f87aa5d3c6f2e84cb5a4239a5fe50480a6ec66b70ab5b1f4ac673" +
                    "0c6c515421b327ec1d69402e53dfb49ad7381eb067b338fd7b0cb22247225d47",
                    "6032d81ebc7497d91097b059e3a06bbcd5dfcdf397f5d4bab167286d51f39ed5"
                ),
                (
                    "cryptography",
                    "atomex",
                    "1257be7bf24216ec9749567c74c017d36c723e69045ec616145ad03b052b697f"
                )
            };

        public static IEnumerable<object[]> Data
        {
            get
            {
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
        public void CanCalculateMac(MacAlgorithm hmacSha256, string key, string data, string hash)
        {
            var keyBytes = Encoding.UTF8.GetBytes(key);
            var dataBytes = Encoding.UTF8.GetBytes(data);
            var hashBytes = hmacSha256.Mac(keyBytes, dataBytes);

            Assert.Equal(Hex.FromString(hash), hashBytes);
        }

        [Theory]
        [MemberData(nameof(Data))]
        public void CanCalculateMacToSpan(MacAlgorithm hmacSha256, string key, string data, string hash)
        {
            var hashBytes = new byte[hash.Length / 2];

            var keyBytes = Encoding.UTF8.GetBytes(key);
            var dataBytes = Encoding.UTF8.GetBytes(data);

            hmacSha256.Mac(keyBytes, dataBytes, hashBytes);

            Assert.Equal(Hex.FromString(hash), hashBytes);
        }

        [Theory]
        [MemberData(nameof(Data))]
        public void CanVerify(MacAlgorithm hmacSha256, string key, string data, string hash)
        {
            var keyBytes = Encoding.UTF8.GetBytes(key);
            var dataBytes = Encoding.UTF8.GetBytes(data);

            var result = hmacSha256.Verify(
                keyBytes,
                dataBytes,
                Hex.FromString(hash));

            Assert.True(result);
        }
    }
}