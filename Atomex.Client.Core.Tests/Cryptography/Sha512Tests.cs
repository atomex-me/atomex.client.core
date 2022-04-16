using System;
using System.Collections.Generic;
using System.Text;
using Xunit;

using Atomex.Common;
using Atomex.Cryptography.Abstract;

namespace Atomex.Cryptography
{
    public class Sha512Tests
    {
        private static readonly IEnumerable<HashAlgorithm> HashAlgorithms = new List<HashAlgorithm>
        {
            new Libsodium.Sha512(),
            new DotNet.Sha512(),
            new Sha512()
        };

        public static IEnumerable<(string data, string hash)> Hashes => new List<(string data, string hash)>
        {
            (
                "" ,
                "cf83e1357eefb8bdf1542850d66d8007d620e4050b5715dc83f4a921d36ce9ce" +
                "47d0d13c5d85f2b0ff8318d2877eec2f63b931bd47417a81a538327af927da3e"
            ),
            (
                "a",
                "1f40fc92da241694750979ee6cf582f2d5d7d28e18335de05abc54d0560e0f53" +
                "02860c652bf08d560252aa5e74210546f369fbbbce8c12cfc7957b2652fe9a75"
            ),
            (
                "simple text",
                "d942780ca9eb34cab2fd11a205936d77c07e55f1239f6d6efb6f1406c7b7887f" +
                "d9b1f2350159e94ce7c295e01704a856d58f08bd8ee1cebf9b3c5ab887dc50a5"
            ),
            (
                "atomex",
                "8101017197f87f0a49987ff0265d1a6a857139f21e02c535c6631be630c02157" +
                "ed90a37b0c1deba9447521d1360f7412fecf31c1940dff957a449763402f31b3"
            ),
            (
                "2609c7c28788898a337c063ff1c3b92275832bddeda014a790d109fad3ba85e2",
                "2d20bb520cee26b327ab1eb79c6fbbd5be24ab2666593dc69c0389ca368d9f8d" +
                "394a8b12cf2a9270f2f40979588bbd2a03f2a2028cec02d133c90ef488e37237"
            ),
            (
                "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855e3b0c4" +
                "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855e3b0c4" +
                "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855e3b0c4" +
                "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855e3b0c4" +
                "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855e3b0c4" +
                "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855e3b0c4" +
                "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855e3b0c4",
                "5504e210851caa0fcba1a4182a955c8de50810b85447813967c7f5ae878a74a2" +
                "21ef3900a4ca3e5ec8ea8eb2e0247a066f1424bdf2ad6165e03f6b9a030aa6c5"
            )
        };

        public static IEnumerable<object[]> Data
        {
            get
            {
                var data = new List<object[]>();

                foreach (var hashAlgorithm in HashAlgorithms)
                    foreach (var hash in Hashes)
                        data.Add(new object[]
                        {
                            hashAlgorithm,
                            hash.data,
                            hash.hash
                        });

                return data;
            }
        }

        [Theory]
        [MemberData(nameof(Data))]
        public void CanCalculateHash(HashAlgorithm hashAlgorithm, string data, string hash)
        {
            var hashBytes = hashAlgorithm.Hash(Encoding.UTF8.GetBytes(data));

            Assert.Equal(Hex.FromString(hash), hashBytes);
        }

        [Theory]
        [MemberData(nameof(Data))]
        public void CanCalculateHashToSpan(HashAlgorithm hashAlgorithm, string data, string hash)
        {
            var hashBytes = new byte[hash.Length / 2];

            hashAlgorithm.Hash(Encoding.UTF8.GetBytes(data), hashBytes);

            Assert.Equal(Hex.FromString(hash), hashBytes);
        }

        [Theory]
        [MemberData(nameof(Data))]
        public void CanVerify(HashAlgorithm hashAlgorithm, string data, string hash)
        {
            var result = hashAlgorithm.Verify(
                Encoding.UTF8.GetBytes(data),
                Hex.FromString(hash));

            Assert.True(result);
        }

        [Theory]
        [MemberData(nameof(Data))]
        public void CanCalculateIncrementalHash(HashAlgorithm hashAlgorithm, string data, string hash)
        {
            var dataBytes = Encoding.UTF8.GetBytes(data);
            var dataBytesSpan = new ReadOnlySpan<byte>(dataBytes);

            using var incrementalHash = hashAlgorithm.CreateIncrementalHash();
            incrementalHash.Update(dataBytesSpan[..(dataBytes.Length / 2)]);
            incrementalHash.Update(dataBytesSpan[(dataBytes.Length / 2)..]);

            var hashBytes = incrementalHash.Finalize();

            Assert.Equal(Hex.FromString(hash), hashBytes);
        }

        [Theory]
        [MemberData(nameof(Data))]
        public void CanCalculateIncrementalHashToSpan(HashAlgorithm hashAlgorithm, string data, string hash)
        {
            var dataBytes = Encoding.UTF8.GetBytes(data);
            var dataBytesSpan = new ReadOnlySpan<byte>(dataBytes);

            using var incrementalHash = hashAlgorithm.CreateIncrementalHash();
            incrementalHash.Update(dataBytesSpan[..(dataBytes.Length / 2)]);
            incrementalHash.Update(dataBytesSpan[(dataBytes.Length / 2)..]);

            var hashBytes = new byte[hash.Length / 2];
            incrementalHash.Finalize(hashBytes);

            Assert.Equal(Hex.FromString(hash), hashBytes);
        }

        [Theory]
        [MemberData(nameof(Data))]
        public void CanReinitiateIncrementalHash(HashAlgorithm hashAlgorithm, string data, string hash)
        {
            using var incrementalHash = hashAlgorithm.CreateIncrementalHash();
            incrementalHash.Update(Encoding.UTF8.GetBytes(data));
            var hashBytes = incrementalHash.Finalize();

            Assert.Equal(Hex.FromString(hash), hashBytes);

            incrementalHash.Initialize();
            incrementalHash.Update(Encoding.UTF8.GetBytes(data));
            hashBytes = incrementalHash.Finalize();

            Assert.Equal(Hex.FromString(hash), hashBytes);
        }

        [Theory]
        [MemberData(nameof(Data))]
        public void CanReinitiateSeveralTimes(HashAlgorithm hashAlgorithm, string data, string hash)
        {
            using var incrementalHash = hashAlgorithm.CreateIncrementalHash();
            incrementalHash.Update(Encoding.UTF8.GetBytes(data));
            var hashBytes1 = incrementalHash.Finalize();

            incrementalHash.Initialize();
            incrementalHash.Update(Encoding.UTF8.GetBytes(data));
            var hashBytes2 = incrementalHash.Finalize();

            incrementalHash.Initialize();
            incrementalHash.Initialize();
            incrementalHash.Update(Encoding.UTF8.GetBytes(data));
            var hashBytes3 = incrementalHash.Finalize();

            var expectedHashBytes = Hex.FromString(hash);

            Assert.Equal(expectedHashBytes, hashBytes1);
            Assert.Equal(expectedHashBytes, hashBytes2);
            Assert.Equal(expectedHashBytes, hashBytes3);
        }
    }
}