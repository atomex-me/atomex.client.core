using System;
using System.Collections.Generic;
using System.Text;
using Xunit;

using Atomex.Cryptography.Abstract;
using Atomex.Wallets.Abstract;
using Atomex.Wallet;

namespace Atomex.Client.Core.Tests.Wallets
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

            var publicKey = await wallet.GetPublicKeyAsync(Atomex.Wallets.Wallet.SingleKeyPath);

            Assert.NotNull(publicKey);
        }

        [Fact]
        public async void CanSignAndVerifyHashAsync()
        {
            var hash = HashAlgorithm.Sha256.Hash(Encoding.UTF8.GetBytes("test data to sign"));

            using var wallet = CreateWallet();

            var signature = await wallet.SignHashAsync(hash, Atomex.Wallets.Wallet.SingleKeyPath);

            Assert.NotNull(signature);

            var isVerified = await wallet.VerifyHashAsync(hash, signature, Atomex.Wallets.Wallet.SingleKeyPath);

            Assert.True(isVerified);
        }

        [Fact]
        public async void CanSignAndVerifyManyHashesAsync()
        {
            var hashes = new List<ReadOnlyMemory<byte>>
            {
                HashAlgorithm.Sha256.Hash(Encoding.UTF8.GetBytes("test data to sign")),
                HashAlgorithm.Sha256.Hash(Encoding.UTF8.GetBytes("test data to sign"))
            };

            var keyPathes = new List<string> { Atomex.Wallets.Wallet.SingleKeyPath, Atomex.Wallets.Wallet.SingleKeyPath };

            using var wallet = CreateWallet();

            var signatures = await wallet.SignHashAsync(hashes, keyPathes);

            Assert.NotNull(signatures);
            Assert.Equal(hashes.Count, signatures.Count);

            for (var i = 0; i < signatures.Count; ++i)
            {
                var isVerified = await wallet.VerifyHashAsync(hashes[i], signatures[i], keyPathes[i]);

                Assert.True(isVerified);
            }
        }
    }
}