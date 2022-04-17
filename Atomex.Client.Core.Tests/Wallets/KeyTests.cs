using System.Text;
using Xunit;

using Atomex.Common.Libsodium;
using Atomex.Cryptography.Abstract;

namespace Atomex.Wallets
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
            using var key = CreateKey(KeySize, out var _);

            var privateKey = key.GetPrivateKey();

            Assert.NotNull(privateKey);
        }

        [Fact]
        public void CanGetPublicKey()
        {
            using var key = CreateKey(KeySize, out var _);

            var publicKey = key.GetPublicKey();

            Assert.NotNull(publicKey);
        }

        [Fact]
        public void CanSignAndVerify()
        {
            using var key = CreateKey(KeySize, out var _);

            var data = Encoding.UTF8.GetBytes("test data to sign");

            if (key.SignDataType == SignDataType.Hash)
                data = HashAlgorithm.Sha256.Hash(data);

            var signature = key.Sign(data);
            var isVerified = key.Verify(data, signature);

            Assert.NotNull(signature);
            Assert.True(isVerified);
        }

        [Fact]
        public async void CanSignAndVerifyAsync()
        {
            using var key = CreateKey(KeySize, out var _);

            var data = Encoding.UTF8.GetBytes("test data to sign");

            if (key.SignDataType == SignDataType.Hash)
                data = HashAlgorithm.Sha256.Hash(data);

            var signature = await key.SignAsync(data);
            var isVerified = await key.VerifyAsync(data, signature);

            Assert.NotNull(signature);
            Assert.True(isVerified);
        }
    }
}