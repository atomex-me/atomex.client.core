#nullable enable

using System;
using System.IO;
using System.Security;
using System.Threading;
using System.Threading.Tasks;

using NBitcoin;
using Serilog;

using Atomex.Common;
using Atomex.Wallet.Abstract;
using Atomex.Wallets;
using Atomex.Wallets.Abstract;
using Network = Atomex.Core.Network;

namespace Atomex.Wallet
{
    public class HdWallet : IHdWallet
    {
        public HdKeyStorage KeyStorage { get; }

        public string PathToWallet { get; set; }
        public Network Network => KeyStorage.Network;
        public bool IsLocked => KeyStorage.IsLocked;

        private HdWallet(string pathToWallet, SecureString password)
        {
            PathToWallet = FileSystem.Current.ToFullPath(pathToWallet);

            KeyStorage = HdKeyStorageLoader
                .LoadFromFile(pathToWallet, password)
                .Unlock(password);
        }

        public HdWallet(
            string mnemonic,
            Wordlist wordList,
            SecureString? passPhrase = null,
            Network network = Network.MainNet)
        {
            PathToWallet = FileSystem.Current.ToFullPath(string.Empty);

            KeyStorage = new HdKeyStorage(
                mnemonic: mnemonic,
                wordList: wordList,
                passPhrase: passPhrase,
                network: network);
        }

        public void Lock() =>
            KeyStorage.Lock();

        public void Unlock(SecureString password) =>
            KeyStorage.Unlock(password);

        public Task EncryptAsync(SecureString password) =>
            KeyStorage.EncryptAsync(password);

        public WalletAddress GetAddress(
            CurrencyConfig currency,
            string keyPath,
            int keyType)
        {
            var publicKey = KeyStorage.GetPublicKey(
                currency: currency,
                keyPath: keyPath,
                keyType: keyType) ?? throw new Exception($"Can't get public key for {currency?.Name} with key path {keyPath} and key type {keyType}");

            var address = currency.AddressFromKey(publicKey, keyType);

            return new WalletAddress
            {
                Currency = currency.Name,
                Address = address,
                KeyPath = keyPath,
                KeyIndex = keyPath.GetIndex(
                    keyPathPattern: currency.GetKeyPathPattern(keyType),
                    indexPattern: KeyPathExtensions.IndexPattern),
                KeyType = keyType
            };
        }

        public byte[] GetPublicKey(
            CurrencyConfig currency,
            string keyPath,
            int keyType) =>
            KeyStorage.GetPublicKey(currency, keyPath, keyType);

        public byte[] GetServicePublicKey(uint index) =>
            KeyStorage.GetServicePublicKey(index);

        public Task<byte[]> SignHashAsync(
            byte[] hash,
            WalletAddress address,
            CurrencyConfig currencyConfig,
            CancellationToken cancellationToken = default)
        {
            if (hash == null)
                throw new ArgumentNullException(nameof(hash));

            if (address == null)
                throw new ArgumentNullException(nameof(address));

            Log.Verbose("Sign request for hash {@hash}", hash.ToHexString());

            var signature = KeyStorage.SignHash(
                currency: currencyConfig,
                hash: hash,
                keyPath: address.KeyPath,
                keyType: address.KeyType);

            Log.Verbose("Hash signature in base64: {@signature}", Convert.ToBase64String(signature));

            if (!KeyStorage.VerifyHash(
                currency: currencyConfig,
                hash: hash,
                signature: signature,
                keyPath: address.KeyPath,
                keyType: address.KeyType))
            {
                Log.Fatal("Signature verification failed for {@curr}", currencyConfig?.Name);

                throw new Exception($"Signature verification failed for {currencyConfig?.Name}");
            }

            Log.Verbose("Hash successfully signed");

            return Task.FromResult(signature);
        }

        public Task<byte[]> SignByServiceKeyAsync(
            byte[] data,
            uint keyIndex,
            CancellationToken cancellationToken = default)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));

            Log.Verbose("Service sign request for data {@data} with key index {@index}",
                data.ToHexString(),
                keyIndex);

            var signature = KeyStorage.SignByServiceKey(
                data: data,
                chain: 0,
                index: keyIndex);

            Log.Verbose("Signature in base64: {@signature}", Convert.ToBase64String(signature));

            if (!KeyStorage.VerifyByServiceKey(data, signature, chain: 0, index: keyIndex))
            {
                Log.Fatal("Service key signature verification failed");

                throw new Exception("ervice key signature verification failed");
            }

            Log.Verbose("Data successfully signed by service key");

            return Task.FromResult(signature);
        }

        public byte[] GetDeterministicSecret(CurrencyConfig currency, DateTime timeStamp) =>
            KeyStorage.GetDeterministicSecret(currency, timeStamp);

        public static HdWallet LoadFromFile(string pathToWallet, SecureString password) =>
            new(pathToWallet, password);

        public bool SaveToFile(string pathToWallet, SecureString password)
        {
            var walletDirectory = Path.GetDirectoryName(pathToWallet);

            if (walletDirectory == null)
                throw new InvalidOperationException();

            if (!Directory.Exists(walletDirectory))
                Directory.CreateDirectory(walletDirectory);

            return KeyStorage.SaveToFile(pathToWallet, password);
        }
    }
}