using System.Text;
using Xunit;

using Atomex.Common.Libsodium;
using Atomex.Cryptography;
using Atomex.Cryptography.Abstract;

namespace Atomex.Client.Core.Tests.Wallets
{
    public abstract class KeyTests<T> where T : IKey
    {
        public abstract int KeySize { get; }
        public abstract int SignatureSize { get; }

        protected KeyTests()
        {
            Sodium.Initialize();
        }

        public abstract T CreateKey(int keySize, out byte[] seed);

        [Fact]
        public void CanBeCreated()
        {
            using var key = CreateKey(KeySize, out var _);

            Assert.NotNull(key);
        }

        [Fact]
        public void CanGetPrivateKey()
        {
            using var key = CreateKey(KeySize, out var seed);

            var privateKey = key.GetPrivateKey();

            Assert.NotNull(privateKey);
            //Assert.Equal(seed, privateKey.ToUnsecuredBytes());
        }

        [Fact]
        public void CanGetPublicKey()
        {
            using var key = CreateKey(KeySize, out var _);

            var publicKey = key.GetPublicKey();

            Assert.NotNull(publicKey);
        }

        [Fact]
        public void CanSignAndVerifyHash()
        {
            var hash = HashAlgorithm.Sha256.Hash(Encoding.UTF8.GetBytes("test data to sign"));

            using var key = CreateKey(KeySize, out var _);

            var signature = key.SignHash(hash);
            var isVerified = key.VerifyHash(hash, signature);

            Assert.NotNull(signature);
            Assert.True(isVerified);
        }

        [Fact]
        public async void CanSignAndVerifyHashAsync()
        {
            var hash = HashAlgorithm.Sha256.Hash(Encoding.UTF8.GetBytes("test data to sign"));

            using var key = CreateKey(KeySize, out var _);

            var signature = await key.SignHashAsync(hash);
            var isVerified = await key.VerifyHashAsync(hash, signature);

            Assert.NotNull(signature);
            Assert.True(isVerified);
        }
    }
}