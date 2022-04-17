using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Xunit;

using Atomex.Wallets.Abstract;
using Atomex.Cryptography.Abstract;

namespace Atomex.Wallets
{
    public abstract class WalletTests<T> where T : IWallet
    {
        public abstract IWallet CreateWallet();

        [Fact]
        public async void CantUseInvalidKeyPath()
        {
            using var wallet = CreateWallet();

            await Assert.ThrowsAsync<ArgumentException>(() => wallet.GetPublicKeyAsync(null));
            await Assert.ThrowsAsync<ArgumentException>(() => wallet.GetPublicKeyAsync(""));
            await Assert.ThrowsAsync<ArgumentException>(() => wallet.GetPublicKeyAsync("m/44'/0'"));
        }

        [Fact]
        public async void CanGetPublicKeyAsync()
        {
            using var wallet = CreateWallet();

            var publicKey = await wallet.GetPublicKeyAsync(Wallet.SingleKeyPath);

            Assert.NotNull(publicKey);
        }

        [Fact]
        public async void CanSignAndVerifyAsync()
        {
            using var wallet = CreateWallet();

            var data = Encoding.UTF8.GetBytes("test data to sign");

            if (wallet.SignDataType == SignDataType.Hash)
                data = HashAlgorithm.Sha256.Hash(data);

            var signature = await wallet.SignAsync(data, Wallet.SingleKeyPath);

            Assert.NotNull(signature);

            var isVerified = await wallet.VerifyAsync(data, signature, Wallet.SingleKeyPath);

            Assert.True(isVerified);
        }

        [Fact]
        public async void CanSignAndVerifySeveralDataAsync()
        {
            using var wallet = CreateWallet();

            var data = new List<ReadOnlyMemory<byte>>
            {
                Encoding.UTF8.GetBytes("test data to sign 1"),
                Encoding.UTF8.GetBytes("test data to sign 2")
            };

            if (wallet.SignDataType == SignDataType.Hash)
                data = data
                    .Select(d => new ReadOnlyMemory<byte>(HashAlgorithm.Sha256.Hash(d.Span)))
                    .ToList();

            var keyPathes = new List<string>
            {
                Wallet.SingleKeyPath,
                Wallet.SingleKeyPath
            };

            var signatures = await wallet.SignAsync(data, keyPathes);

            Assert.NotNull(signatures);
            Assert.Equal(data.Count, signatures.Count);

            for (var i = 0; i < signatures.Count; ++i)
            {
                var isVerified = await wallet.VerifyAsync(data[i], signatures[i], keyPathes[i]);

                Assert.True(isVerified);
            }
        }
    }
}