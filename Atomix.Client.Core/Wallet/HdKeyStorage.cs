using System;
using System.Collections.Generic;
using System.IO;
using System.Security;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Atomix.Common;
using Atomix.Common.Json;
using Atomix.Core.Entities;
using Atomix.Cryptography;
using Atomix.Wallet.Abstract;
using Atomix.Wallet.Bip;
using Atomix.Wallet.BitcoinBased;
using Atomix.Wallet.KeyData;
using NBitcoin;
using Newtonsoft.Json;
using Serilog;

namespace Atomix.Wallet
{
    public class HdKeyStorage : IPrivateKeyStorage
    {
        private const int MaxFileSizeInBytes = 10 * 1024 * 1024; // 10 Mb
        private const int PasswordHashIterations = 5;
        private const int ServicePurpose = 777;

        [JsonProperty("Keys")]
        private Dictionary<uint, IHdKeyData> _keys;

        [JsonProperty("ServiceKey")]
        private IHdKeyData _serviceKey;

        [JsonIgnore]
        public bool IsLocked => _serviceKey.IsLocked;

        [JsonIgnore]
        public IKeyIndexCache Cache { get; private set; }

        public HdKeyStorage() { }

        public HdKeyStorage(string mnemonic, Wordlist wordList = null, SecureString passPhrase = null, uint account = 0)
        {
            _keys = new Dictionary<uint, IHdKeyData>
            {
                { Bip44.Bitcoin, new BitcoinBasedHdKeyData(
                    mnemonic: mnemonic,
                    wordList: wordList,
                    passPhrase: passPhrase,
                    purpose: Bip44.Purpose,
                    currency: Bip44.Bitcoin,
                    account: account)},

                { Bip44.Litecoin, new BitcoinBasedHdKeyData(
                    mnemonic: mnemonic,
                    wordList: wordList,
                    passPhrase: passPhrase,
                    purpose: Bip44.Purpose,
                    currency: Bip44.Litecoin,
                    account: account)},

                { Bip44.Ethereum, new EthereumHdKeyData(
                    mnemonic: mnemonic,
                    passPhrase: passPhrase,
                    account: account)},

                { Bip44.Tezos, new TezosHdKeyData(
                    mnemonic: mnemonic,
                    passPhrase: passPhrase,
                    account: account)}
            };

            _serviceKey = new BitcoinBasedHdKeyData(
                mnemonic: mnemonic,
                passPhrase: passPhrase,
                wordList: wordList,
                purpose: ServicePurpose,
                currency: 0,
                account: account);
        }

        public HdKeyStorage UseCache(IKeyIndexCache cache)
        {
            Cache = cache;
            return this;
        }

        public void Lock()
        {
            foreach (var key in _keys)
                key.Value.Lock();

            _serviceKey.Lock();
        }

        public void Unlock(SecureString password)
        {
            foreach (var key in _keys)
                key.Value.Unlock(password);

            _serviceKey.Unlock(password);
        }

        public async Task EncryptAsync(SecureString password)
        {
            foreach (var key in _keys)
                await key.Value.EncryptAsync(password)
                    .ConfigureAwait(false);

            await _serviceKey.EncryptAsync(password)
                .ConfigureAwait(false);
        }

        public byte[] GetPublicKey(Currency currency, KeyIndex keyIndex)
        {
            return GetPublicKey(currency, keyIndex.Chain, keyIndex.Index);
        }

        public byte[] GetPublicKey(Currency currency, uint chain, uint index)
        {
            var publicKeyBytes = _keys[currency.Bip44Code].GetPublicKey(chain, index);

            var address = currency.AddressFromKey(publicKeyBytes);

            Cache?.Add(address, chain, index);

            return publicKeyBytes;
        }

        public byte[] GetServicePublicKey(uint index)
        {
            return _serviceKey.GetPublicKey(chain: 0, index: index);
        }

        public byte[] GetPrivateKey(Currency currency, KeyIndex keyIndex)
        {
            return _keys[currency.Bip44Code].GetPrivateKey(keyIndex);
        }

        public byte[] SignHash(Currency currency, byte[] hash, KeyIndex keyIndex)
        {
            return _keys[currency.Bip44Code].SignHash(hash, keyIndex);
        }

        public byte[] SignMessage(Currency currency, byte[] data, KeyIndex keyIndex)
        {
            return _keys[currency.Bip44Code].SignMessage(data, keyIndex);
        }

        public byte[] SignMessageByServiceKey(byte[] data, uint chain, uint index)
        {
            return _serviceKey.SignMessage(data, chain, index);
        }

        public bool VerifyHash(Currency currency, byte[] hash, byte[] signature, KeyIndex keyIndex)
        {
            return _keys[currency.Bip44Code].VerifyHash(hash, signature, keyIndex);
        }

        public bool VerifyMessage(Currency currency, byte[] data, byte[] signature, KeyIndex keyIndex)
        {
            return _keys[currency.Bip44Code].VerifyMessage(data, signature, keyIndex);
        }

        public bool VerifyMessageByServiceKey(byte[] data, byte[] signature, uint chain, uint index)
        {
            return _serviceKey.VerifyMessage(data, signature, chain, index);
        }

        public Task<KeyIndex> RecoverKeyIndexAsync(
            WalletAddress walletAddress,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            return RecoverKeyIndexAsync(
                walletAddress.Currency,
                walletAddress.Address,
                cancellationToken);
        }

        public async Task<KeyIndex> RecoverKeyIndexAsync(
            Currency currency,
            string address,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            var keyIndex = Cache?.IndexByAddress(address);

            if (keyIndex != null)
                return keyIndex;

            await Task.Factory.StartNew(() =>
            {
                try
                {
                    var options = new ParallelOptions();// {CancellationToken = cancellationToken};

                    // Oops, only brute force can help us
                    Parallel.ForEach(new[] {Bip44.Internal, Bip44.External}, options, (chain, state) =>
                    {
                        var index = 0u;

                        while (true)
                        {
                            if (state.IsStopped)
                                break;

                            if (cancellationToken.IsCancellationRequested) {
                                if (!state.IsStopped)
                                    state.Stop();
                                break;
                            }

                            var publicKeyBytes = GetPublicKey(currency, chain, index);

                            var addressFromKey = currency.AddressFromKey(publicKeyBytes);

                            if (addressFromKey.ToLowerInvariant().Equals(address.ToLowerInvariant()))
                            {
                                keyIndex = new KeyIndex(chain, index);
                                state.Stop();
                                break;
                            }

                            index++;
                        }
                    });
                }
                catch (OperationCanceledException)
                {
                    Log.Debug("Recover key routine canceled");
                }
            }, cancellationToken).ConfigureAwait(false);

            return keyIndex;
        }

        public static HdKeyStorage LoadFromFile(string pathToFile, SecureString password)
        {
            if (!File.Exists(pathToFile))
                throw new FileNotFoundException($"File {pathToFile} not found.");

            if (new FileInfo(pathToFile).Length > MaxFileSizeInBytes)
                throw new Exception("File is too large");

            HdKeyStorage result = null;

            try
            {
                var passwordHash = SessionPasswordHelper.GetSessionPasswordBytes(password, PasswordHashIterations);
                var encryptedBytes = File.ReadAllBytes(pathToFile);
                var decryptedBytes = Aes.Decrypt(encryptedBytes, passwordHash);
                var json = Encoding.UTF8.GetString(decryptedBytes);

                result = JsonConvert.DeserializeObject<HdKeyStorage>(json,
                    new HdKeyDataConverter());
            }
            catch (Exception e)
            {
                Log.Error(e, "HdKeyStorage data loading error");
                throw e;
            }

            return result;
        }

        public void SaveToFile(string pathToFile, SecureString password)
        {
            try
            {
                var serialized = JsonConvert.SerializeObject(this, Formatting.Indented, new HdKeyDataConverter());
                var serializedBytes = Encoding.UTF8.GetBytes(serialized);

                var passwordHash = SessionPasswordHelper.GetSessionPasswordBytes(password, PasswordHashIterations);
                var encryptedBytes = Aes.Encrypt(serializedBytes, passwordHash);

                File.WriteAllBytes(pathToFile, encryptedBytes);
            }
            catch (Exception e)
            {
                Log.Error(e, "HdKeyStorage save to file error");
            }
        }
    }
}