using System;
using System.Text;
using System.Collections.Generic;
using Xunit;

using Atomex.Common;
using Atomex.Cryptography;
using Atomex.Cryptography.Abstract;

namespace Atomex.Client.Core.Tests.Cryptography
{
    public class Sha256Tests
    {
        private const int HashSize = 32;

        public static IEnumerable<HashAlgorithm> HashAlgorithms = new List<HashAlgorithm>
        {
            new Atomex.Cryptography.Libsodium.Sha256(),
            new Atomex.Cryptography.DotNet.Sha256(),
            new Sha256()
        };

        public static IEnumerable<(string data, string hash)> Hashes => new List<(string data, string hash)>
        {
            (
                "" ,
                "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855"
            ),
            (
                "a",
                "ca978112ca1bbdcafac231b39a23dc4da786eff8147c4e72b9807785afee48bb"
            ),
            (
                "simple text",
                "2609c7c28788898a337c063ff1c3b92275832bddeda014a790d109fad3ba85e2"
            ),
            (
                "atomex",
                "b6d757e081b477638f2c27ee697d784ca39f6e03845e4a94f70ff484fb00301c"
            ),
            (
                "2609c7c28788898a337c063ff1c3b92275832bddeda014a790d109fad3ba85e2",
                "1ae2bef7eecfb1deed87809815cac91eab0f024c060b56562b6920796075dcf7"
            ),
            (
                "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855" +
                "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855" +
                "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855" +
                "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855" +
                "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855" +
                "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855" +
                "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
                "8d18f7fed7124c1c71cd6253bb00be97c1ac696b46b342db6e5c6a51a93718d9"
            )
        };

        public static IEnumerable<object[]> Data
        {
            get {
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
        public void CanCalculateHash(HashAlgorithm sha256, string data, string hash)
        {
            var hashBytes = sha256.Hash(Encoding.UTF8.GetBytes(data));

            Assert.Equal(Hex.FromString(hash), hashBytes);
        }

        [Theory]
        [MemberData(nameof(Data))]
        public void CanCalculateHashToSpan(HashAlgorithm sha256, string data, string hash)
        {
            var hashBytes = new byte[HashSize];

            sha256.Hash(Encoding.UTF8.GetBytes(data), hashBytes);

            Assert.Equal(Hex.FromString(hash), hashBytes);
        }

        [Theory]
        [MemberData(nameof(Data))]
        public void CanVerify(HashAlgorithm sha256, string data, string hash)
        {
            var result = sha256.Verify(
                Encoding.UTF8.GetBytes(data),
                Hex.FromString(hash));

            Assert.True(result);
        }

        [Theory]
        [MemberData(nameof(Data))]
        public void CanCalculateIncrementalHash(HashAlgorithm sha256, string data, string hash)
        {
            var dataBytes = Encoding.UTF8.GetBytes(data);
            var dataBytesSpan = new ReadOnlySpan<byte>(dataBytes);

            using var incrementalHash = sha256.CreateIncrementalHash();
            incrementalHash.Update(dataBytesSpan.Slice(0, dataBytes.Length / 2));
            incrementalHash.Update(dataBytesSpan.Slice(dataBytes.Length / 2));

            var hashBytes = incrementalHash.Finalize();

            Assert.Equal(Hex.FromString(hash), hashBytes);
        }

        [Theory]
        [MemberData(nameof(Data))]
        public void CanCalculateIncrementalHashToSpan(HashAlgorithm sha256, string data, string hash)
        {
            var dataBytes = Encoding.UTF8.GetBytes(data);
            var dataBytesSpan = new ReadOnlySpan<byte>(dataBytes);

            using var incrementalHash = sha256.CreateIncrementalHash();
            incrementalHash.Update(dataBytesSpan.Slice(0, dataBytes.Length / 2));
            incrementalHash.Update(dataBytesSpan.Slice(dataBytes.Length / 2));

            var hashBytes = new byte[HashSize];
            incrementalHash.Finalize(hashBytes);

            Assert.Equal(Hex.FromString(hash), hashBytes);
        }

        [Theory]
        [MemberData(nameof(Data))]
        public void CanReinitiateIncrementalHash(HashAlgorithm sha256, string data, string hash)
        {
            using var incrementalHash = sha256.CreateIncrementalHash();
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
        public void CanReinitiateSeveralTimes(HashAlgorithm sha256, string data, string hash)
        {
            using var incrementalHash = sha256.CreateIncrementalHash();
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