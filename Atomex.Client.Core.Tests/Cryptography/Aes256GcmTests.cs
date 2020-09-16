using System.Collections.Generic;
using Xunit;

using Atomex.Cryptography;
using Atomex.Cryptography.Abstract;
using Atomex.Common;

namespace Atomex.Client.Core.Tests.Cryptography
{
    public class Aes256GcmTests
    {
        public static IEnumerable<AeadAlgorithm> AeadAlgorithms = new List<AeadAlgorithm>
        {
            new Atomex.Cryptography.Libsodium.Aes256Gcm(),
            new Atomex.Cryptography.BouncyCastle.Aes256Gcm(),
            new Aes256Gcm()
        };

        public static IEnumerable<(string key, string plain, string associatedData, string iv, string cipher)> Inputs =>
            new List<(string key, string plain, string associatedData, string iv, string cipher)>
            {
                (
                    "E3C08A8F06C6E3AD95A70557B23F7548" +
                    "3CE33021A9C72B7025666204C69C0B72",
                    "08000F101112131415161718191A1B1C" +
                    "1D1E1F202122232425262728292A2B2C" +
                    "2D2E2F303132333435363738393A0002",
                    "D609B1F056637A0D46DF998D88E52E00" +
                    "B2C2846512153524C0895E81",
                    "12153524C0895E81B2C28465",
                    "E2006EB42F5277022D9B19925BC419D7" +
                    "A592666C925FE2EF718EB4E308EFEAA7" +
                    "C5273B394118860A5BE2A97F56AB7836" +
                    "5CA597CDBB3EDB8D1A1151EA0AF7B436"
                ),
            };

        public static IEnumerable<object[]> Data
        {
            get {
                var data = new List<object[]>();

                foreach (var aeadAlgorithm in AeadAlgorithms)
                    foreach (var input in Inputs)
                        data.Add(new object[]
                        {
                            aeadAlgorithm,
                            input.key,
                            input.plain,
                            input.associatedData,
                            input.iv,
                            input.cipher
                        });

                return data;
            }
        }

        [Theory]
        [MemberData(nameof(Data))]
        public void CanEncrypt(
            AeadAlgorithm aes256gcm,
            string key,
            string plain,
            string associatedData,
            string iv,
            string cipher)
        {
            var keyBytes = Hex.FromString(key);
            var plainBytes = Hex.FromString(plain);
            var associatedDataBytes = Hex.FromString(associatedData);
            var ivBytes = Hex.FromString(iv);

            var cipherBytes = aes256gcm.Encrypt(keyBytes, ivBytes, associatedDataBytes, plainBytes);

            var exptectedBytes = Hex.FromString(cipher);

            Assert.Equal(exptectedBytes, cipherBytes);
        }

        [Theory]
        [MemberData(nameof(Data))]
        public void CanDecrypt(
            AeadAlgorithm aes256gcm,
            string key,
            string plain,
            string associatedData,
            string iv,
            string cipher)
        {
            var keyBytes = Hex.FromString(key);
            var cipherBytes = Hex.FromString(cipher);
            var associatedDataBytes = Hex.FromString(associatedData);
            var ivBytes = Hex.FromString(iv);

            var exptectedBytes = Hex.FromString(plain);

            var plainBytes = new byte[exptectedBytes.Length];

            var result = aes256gcm.Decrypt(keyBytes, ivBytes, associatedDataBytes, cipherBytes, plainBytes);

            Assert.True(result);
            Assert.Equal(exptectedBytes, plainBytes);
        }
    }
}