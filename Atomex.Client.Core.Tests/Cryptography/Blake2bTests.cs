using System;
using System.Text;
using System.Collections.Generic;
using Xunit;

using Atomex.Common;
using Atomex.Cryptography.Abstract;

namespace Atomex.Cryptography
{
    public class Blake2bTests
    {
        private static readonly IEnumerable<HashAlgorithm> HashAlgorithms = new List<HashAlgorithm>
        {
            new Libsodium.Blake2b(),
            new BouncyCastle.Blake2b(),
            new Blake2b(),
            new Libsodium.Blake2b(20),
            new BouncyCastle.Blake2b(20),
            new Blake2b(20)
        };

        public static IEnumerable<(string data, string hash)> Hashes => new List<(string data, string hash)>
        {
            (
                "a" ,
                "8928aae63c84d87ea098564d1e03ad813f107add474e56aedd286349c0c03ea4"
            ),
            (
                "a" ,
                "948caa2db61bc4cdb4faf7740cd491f195043914"
            ),
            (
                "simple text" ,
                "045a71332adcd6e26e88b66211751e8af6ec1540bb3210263dffe3853b65a71b"
            ),
            (
                "simple text" ,
                "767ae374ff408892d74ab66f9beaa578c20c1b4a"
            ),
            (
                "2609c7c28788898a337c063ff1c3b92275832bddeda014a790d109fad3ba85e2" +
                "394a8b12cf2a9270f2f40979588bbd2a03f2a2028cec02d133c90ef488e37237",
                "7c6c62096cb2a929285525565ee2242940dc44e2a246185bde841c4ebd60d8c5"
            ),
            (
                "2609c7c28788898a337c063ff1c3b92275832bddeda014a790d109fad3ba85e2" +
                "394a8b12cf2a9270f2f40979588bbd2a03f2a2028cec02d133c90ef488e37237",
                "df38d651c01dbd153f4a3322a8da795d36c6d7b0"
            ),
        };

        public static IEnumerable<object[]> Data
        {
            get
            {
                var data = new List<object[]>();

                foreach (var hashAlgorithm in HashAlgorithms)
                {
                    foreach (var hashParams in Hashes)
                    {
                        if (hashAlgorithm.HashSize == hashParams.hash.Length / 2)
                        {
                            data.Add(new object[]
                            {
                                hashAlgorithm,
                                hashParams.data,
                                hashParams.hash
                            });
                        }
                    }
                }

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

            using var incrementalHash = hashAlgorithm.CreateIncrementalHash(hash.Length / 2);
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

            using var incrementalHash = hashAlgorithm.CreateIncrementalHash(hash.Length / 2);
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
            using var incrementalHash = hashAlgorithm.CreateIncrementalHash(hash.Length / 2);
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
            using var incrementalHash = hashAlgorithm.CreateIncrementalHash(hash.Length / 2);
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