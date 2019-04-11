using System;
using System.Collections.Generic;
using System.IO;
using System.Security;
using System.Threading;
using System.Threading.Tasks;
using Atomix.Blockchain.Abstract;
using Atomix.Common;
using Atomix.Core.Entities;
using Atomix.Wallet.Abstract;
using Atomix.Wallet.Bip;
using NBitcoin;
using Serilog;

namespace Atomix.Wallet
{
    public class HdWallet : IHdWallet
    {
        private HdKeyStorage KeyStorage { get; }

        public IEnumerable<Currency> Currencies => Atomix.Currencies.Available;
        public bool IsLocked => KeyStorage.IsLocked;
        public string PathToWallet { get; set; }

        public HdWallet()
        {
            PathToWallet = string.Empty;

            var wordList = Wordlist.English;
            var mnemonic = new Mnemonic(wordList, WordCount.TwentyFour).ToString();

            KeyStorage = new HdKeyStorage(mnemonic, wordList, new SecureString());
            KeyStorage.UseCache(new KeyIndexCache());
        }

        public HdWallet(string pathToWallet, SecureString password)
        {
            PathToWallet = PathEx.ToFullPath(pathToWallet);

            KeyStorage = HdKeyStorage.LoadFromFile(pathToWallet, password);
            KeyStorage.UseCache(new KeyIndexCache());
            KeyStorage.Unlock(password);
        }

        public HdWallet(string mnemonic, Wordlist wordList, SecureString passPhrase = null)
        {
            PathToWallet = PathEx.ToFullPath(string.Empty);

            KeyStorage = new HdKeyStorage(mnemonic, wordList, passPhrase);
            KeyStorage.UseCache(new KeyIndexCache());
        }

        public void Lock()
        {
            KeyStorage.Lock();
        }

        public void Unlock(SecureString password)
        {
            KeyStorage.Unlock(password);
        }

        public Task EncryptAsync(SecureString password)
        {
            return KeyStorage.EncryptAsync(password);
        }

        public WalletAddress GetAddress(Currency currency, uint chain, uint index)
        {
            var publicKeyBytes = KeyStorage.GetPublicKey(currency, chain, index);

            var address = currency.AddressFromKey(publicKeyBytes);

            return new WalletAddress
            {
                Currency = currency,
                Address = address,
                PublicKey = Convert.ToBase64String(publicKeyBytes),
            };
        }

        public async Task<WalletAddress> GetAddressAsync(
            Currency currency,
            string address,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            var keyIndex = await KeyStorage
                .RecoverKeyIndexAsync(currency, address, cancellationToken)
                .ConfigureAwait(false);

            if (keyIndex == null)
                return null;

            var publicKeyBytes = KeyStorage.GetPublicKey(currency, keyIndex);

            return new WalletAddress
            {
                Currency = currency,
                Address = address,
                PublicKey = Convert.ToBase64String(publicKeyBytes),
            };
        }

        //public async Task<byte[]> GetPrivateKeyAsync(Currency currency, WalletAddress address)
        //{
        //    var keyIndex = await KeyStorage
        //        .RecoverKeyIndexAsync(address)
        //        .ConfigureAwait(false);

        //    if (keyIndex == null) {
        //        Log.Error($"Can't find private key for address {address.Address}");
        //        return null;
        //    }

        //    return KeyStorage.GetPrivateKey(currency, keyIndex);
        //}

        public byte[] GetServicePublicKey(uint index)
        {
            return KeyStorage.GetServicePublicKey(index);
        }

        public WalletAddress GetInternalAddress(Currency currency, uint index)
        {
            return GetAddress(currency, Bip44.Internal, index);
        }

        public WalletAddress GetExternalAddress(Currency currency, uint index)
        {
            return GetAddress(currency, Bip44.External, index);
        }

        public async Task<byte[]> SignAsync(
            byte[] data,
            WalletAddress address,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));

            if (address == null)
                throw new ArgumentNullException(nameof(address));

            Log.Verbose(
                messageTemplate: "Sign request for data {@data} with key for address {@address}", 
                propertyValue0: data.ToHexString(),
                propertyValue1: address.Address);

            if (IsLocked) {
                Log.Warning("Wallet locked");
                return null;
            }

            var keyIndex = await KeyStorage
                .RecoverKeyIndexAsync(address, cancellationToken)
                .ConfigureAwait(false);

            if (keyIndex == null) {
                Log.Error($"Can't find private key for address {address.Address}");
                return null;
            }

            var signature = KeyStorage.SignMessage(address.Currency, data, keyIndex);

            Log.Verbose(
                messageTemplate: "Data signature in base64: {@signature}",
                propertyValue: Convert.ToBase64String(signature));

            if (!KeyStorage.VerifyMessage(address.Currency, data, signature, keyIndex))
            {
                Log.Error("Signature verify error");
                return null;
            }

            Log.Verbose("Data successfully signed");

            return signature;      
        }

        public async Task<bool> SignAsync(
            IInOutTransaction transaction,
            IEnumerable<ITxOutput> spentOutputs,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            if (transaction == null)
                throw new ArgumentNullException(nameof(transaction));

            Log.Verbose("Sign request for transaction {@id}", transaction.Id);

            if (IsLocked)
            {
                Log.Warning("Wallet locked");
                return false;
            }

            await transaction
                .SignAsync(KeyStorage, spentOutputs, cancellationToken)
                .ConfigureAwait(false);

            Log.Verbose(
                messageTemplate: "Transaction {@id} successfully signed",
                propertyValue: transaction.Id);

            return true;
        }

        public async Task<bool> SignAsync(
            IAddressBasedTransaction transaction,
            string address,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            if (transaction == null)
                throw new ArgumentNullException(nameof(transaction));

            Log.Verbose("Sign request for transaction {@id}", transaction.Id);

            if (IsLocked)
            {
                Log.Warning("Wallet locked");
                return false;
            }

            await transaction
                .SignAsync(KeyStorage, address, cancellationToken)
                .ConfigureAwait(false);

            Log.Verbose(
                messageTemplate: "Transaction {@id} successfully signed",
                propertyValue: transaction.Id);

            return true;
        }

        public async Task<byte[]> SignHashAsync(
            byte[] hash,
            WalletAddress address,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            if (hash == null)
                throw new ArgumentNullException(nameof(hash));

            if (address == null)
                throw new ArgumentNullException(nameof(address));

            Log.Verbose(
                messageTemplate: "Sign request for hash {@hash}",
                propertyValue: hash.ToHexString());

            if (IsLocked)
            {
                Log.Warning("Wallet locked");
                return null;
            }

            var keyIndex = await KeyStorage
                .RecoverKeyIndexAsync(address, cancellationToken)
                .ConfigureAwait(false);

            if (keyIndex == null)
            {
                Log.Error($"Can't find private key for address {address.Address}");
                return null;
            }

            var signature = KeyStorage.SignHash(address.Currency, hash, keyIndex);

            Log.Verbose(
                messageTemplate: "Hash signature in base64: {@signature}",
                propertyValue: Convert.ToBase64String(signature));

            if (!KeyStorage.VerifyHash(address.Currency, hash, signature, keyIndex))
            {
                Log.Error("Signature verify error");
                return null;
            }

            Log.Verbose("Hash successfully signed");

            return signature;
        }


        public Task<byte[]> SignByServiceKeyAsync(
            byte[] data,
            uint keyIndex,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));

            Log.Verbose(
                messageTemplate: "Service sign request for data {@data} with key index {@index}",
                propertyValue0: data.ToHexString(),
                propertyValue1: keyIndex);

            if (IsLocked)
            {
                Log.Warning("Wallet locked");
                return null;
            }

            var signature = KeyStorage.SignMessageByServiceKey(
                data: data,
                chain: 0,
                index: keyIndex);

            Log.Verbose(
                messageTemplate: "Signature in base64: {@signature}",
                propertyValue: Convert.ToBase64String(signature));

            if (!KeyStorage.VerifyMessageByServiceKey(data, signature, chain: 0, index: keyIndex))
            {
                Log.Error("Signature verify error");
                return null;
            }

            Log.Verbose("Data successfully signed by service key");

            return Task.FromResult(signature);
        }

        public void SaveToFile(string pathToWallet, SecureString password)
        {
            var walletDirectory = Path.GetDirectoryName(pathToWallet);

            if (walletDirectory == null)
                throw new InvalidOperationException();

            if (!Directory.Exists(walletDirectory))
                Directory.CreateDirectory(walletDirectory);

            KeyStorage.SaveToFile(pathToWallet, password);
        }
    }
}