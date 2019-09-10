using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Atomex.Common;
using Atomex.Core.Entities;
using Atomex.Cryptography;
using Atomex.Wallet.Abstract;
using Atomex.Wallet.Bip;
using NBitcoin;
using Newtonsoft.Json;
using Serilog;
using Aes = Atomex.Cryptography.Aes;
using Network = Atomex.Core.Network;

namespace Atomex.Wallet
{
    public class NonHdKey
    {
        private const int AesKeySize = 256;
        private const int AesSaltSize = 16;
        private const int AesRfc2898Iterations = 1024;

        public uint CurrencyCode { get; set; }
        public string EncryptedSeed { get; set; }

        [JsonIgnore]
        public byte[] Seed { get; set; }

        public void Lock()
        {
            Seed.Clear();
            Seed = null;
        }

        public void Unlock(SecureString password)
        {
            try
            {
                Seed = Aes.Decrypt(
                    encryptedBytes: Hex.FromString(EncryptedSeed),
                    password: password,
                    keySize: AesKeySize,
                    saltSize: AesSaltSize,
                    iterations: AesRfc2898Iterations);
            }
            catch (Exception e)
            {
                Log.Error(e, "Unlock error");
            }
        }

        public void Encrypt(SecureString password)
        {
            try
            {
                EncryptedSeed = Aes.Encrypt(
                        plainBytes: Seed,
                        password: password,
                        keySize: AesKeySize,
                        saltSize: AesSaltSize,
                        iterations: AesRfc2898Iterations)
                    .ToHexString();
            }
            catch (Exception e)
            {
                Log.Error(e, "Encrypt error");
            }
        }
    }

    public class HdKeyStorage : IKeyStorage
    {
        public const string CurrentVersion = "1.0.0.0";
        public const int NonHdKeysChain = -1;
        private const int ServicePurpose = 777;
        private const int MaxFileSizeInBytes = 100 * 1024 * 1024; // 100 Mb

        private const int AesKeySize = 256;
        private const int AesSaltSize = 16;
        private const int AesRfc2898Iterations = 1024;

        public Network Network { get; set; }
        public string EncryptedSeed { get; set; }
        public string Version { get; set; }
        public IList<NonHdKey> NonHdKeys { get; } = new List<NonHdKey>();

        [JsonIgnore]
        private byte[] Seed { get; set; }

        [JsonIgnore]
        public bool IsLocked => Seed == null;

        public HdKeyStorage()
        {
        }

        public HdKeyStorage(byte[] seed, Network network = Network.MainNet)
        {
            Version = CurrentVersion;
            Seed = seed;
            Network = network;
        }

        public HdKeyStorage(
            string mnemonic,
            Wordlist wordList = null,
            SecureString passPhrase = null,
            Network network = Network.MainNet)
        {
            Version = CurrentVersion;
            Seed = new Mnemonic(mnemonic, wordList)
                .DeriveSeed(passPhrase.ToUnsecuredString());
            Network = network;
        }

        public void Lock()
        {
            Seed.Clear();
            Seed = null;

            foreach (var singleKey in NonHdKeys)
                singleKey.Lock();
        }

        public HdKeyStorage Unlock(SecureString password)
        {
            try
            {
                Seed = Aes.Decrypt(
                    encryptedBytes: Hex.FromString(EncryptedSeed),
                    password: password,
                    keySize: AesKeySize,
                    saltSize: AesSaltSize,
                    iterations: AesRfc2898Iterations);

                foreach (var singleKey in NonHdKeys)
                    singleKey.Unlock(password);
            }
            catch (Exception e)
            {
                Log.Error(e, "Unlock error");
            }

            return this;
        }

        public void Encrypt(SecureString password)
        {
            try
            {
                EncryptedSeed = Aes.Encrypt(
                        plainBytes: Seed,
                        password: password,
                        keySize: AesKeySize,
                        saltSize: AesSaltSize,
                        iterations: AesRfc2898Iterations)
                    .ToHexString();

                foreach (var singleKey in NonHdKeys)
                    singleKey.Encrypt(password);
            }
            catch (Exception e)
            {
                Log.Error(e, "Encrypt error");
            }
        }

        public Task EncryptAsync(SecureString password)
        {
            return Task.Factory.StartNew(() => Encrypt(password));
        }

        private IExtKey GetExtKey(
            Currency currency,
            int purpose,
            int chain,
            uint index)
        {
            return currency
                .CreateExtKey(Seed)
                .Derive(new KeyPath(path: $"m/{purpose}'/{currency.Bip44Code}'/0'/{chain}/{index}"));
        }

        private IKey GetNonHdKey(Currency currency, uint index)
        {
            var nonHdKeys = NonHdKeys
                .Where(s => s.CurrencyCode == currency.Bip44Code)
                .ToList();

            return index < nonHdKeys.Count
                ? currency.CreateKey(nonHdKeys[(int)index].Seed)
                : null;
        }

        public byte[] GetPublicKey(Currency currency, KeyIndex keyIndex)
        {
            return GetPublicKey(currency, keyIndex.Chain, keyIndex.Index);
        }

        public byte[] GetPublicKey(Currency currency, int chain, uint index)
        {
            if (chain == NonHdKeysChain)
                return GetNonHdPublicKey(currency, index);

            var extKey = GetExtKey(
                currency: currency,
                purpose: Bip44.Purpose,
                chain: chain,
                index: index);

            extKey.GetPublicKey(out var publicKey);

            return publicKey;
        }

        private byte[] GetNonHdPublicKey(Currency currency, uint index)
        {
            var key = GetNonHdKey(currency, index);

            if (key == null)
                return null;

            key.GetPublicKey(out var publicKey);

            return publicKey;
        }

        public byte[] GetServicePublicKey(uint index)
        {
            var extKey = BitcoinBasedCurrency
                .CreateExtKeyFromSeed(Seed)
                .Derive(new KeyPath(path: $"m/{ServicePurpose}'/0'/0'/0/{index}"));

            extKey.GetPublicKey(out var publicKey);

            return publicKey;
        }

        public byte[] GetPrivateKey(Currency currency, KeyIndex keyIndex)
        {
            if (keyIndex.Chain == NonHdKeysChain)
                return GetNonHdPrivateKey(currency, keyIndex.Index);

            var extKey = GetExtKey(
                currency: currency,
                purpose: Bip44.Purpose,
                chain: keyIndex.Chain,
                index: keyIndex.Index);

            extKey.GetPrivateKey(out var privateKey);

            return privateKey;
        }

        private byte[] GetNonHdPrivateKey(Currency currency, uint index)
        {
            var key = GetNonHdKey(currency, index);

            if (key == null)
                return null;

            key.GetPrivateKey(out var privateKey);

            return privateKey;
        }

        public byte[] SignHash(Currency currency, byte[] hash, KeyIndex keyIndex)
        {
            if (keyIndex.Chain == NonHdKeysChain)
                return GetNonHdKey(currency, keyIndex.Index)
                    .SignHash(hash);

            return GetExtKey(
                    currency: currency,
                    purpose: Bip44.Purpose,
                    chain: keyIndex.Chain,
                    index: keyIndex.Index)
                .SignHash(hash);
        }

        public byte[] SignMessage(Currency currency, byte[] data, KeyIndex keyIndex)
        {
            if (keyIndex.Chain == NonHdKeysChain)
                return GetNonHdKey(currency, keyIndex.Index)
                    .SignMessage(data);

            return GetExtKey(
                    currency: currency,
                    purpose: Bip44.Purpose,
                    chain: keyIndex.Chain,
                    index: keyIndex.Index)
                .SignMessage(data);
        }

        public byte[] SignMessageByServiceKey(byte[] data, int chain, uint index)
        {
            return BitcoinBasedCurrency
                .CreateExtKeyFromSeed(Seed)
                .Derive(new KeyPath(path: $"m/{ServicePurpose}'/0'/0'/{chain}/{index}"))
                .SignMessage(data);
        }

        public bool VerifyHash(
            Currency currency,
            byte[] hash,
            byte[] signature,
            KeyIndex keyIndex)
        {
            if (keyIndex.Chain == NonHdKeysChain)
                return GetNonHdKey(currency, keyIndex.Index)
                    .VerifyHash(hash, signature);

            return GetExtKey(
                    currency: currency,
                    purpose: Bip44.Purpose,
                    chain: keyIndex.Chain,
                    index: keyIndex.Index)
                .VerifyHash(hash, signature);
        }

        public bool VerifyMessage(
            Currency currency,
            byte[] data,
            byte[] signature,
            KeyIndex keyIndex)
        {
            if (keyIndex.Chain == NonHdKeysChain)
                return GetNonHdKey(currency, keyIndex.Index)
                    .VerifyMessage(data, signature);

            return GetExtKey(
                    currency: currency,
                    purpose: Bip44.Purpose,
                    chain: keyIndex.Chain,
                    index: keyIndex.Index)
                .VerifyMessage(data, signature);
        }

        public bool VerifyMessageByServiceKey(
            byte[] data,
            byte[] signature,
            int chain,
            uint index)
        {
            return BitcoinBasedCurrency
                .CreateExtKeyFromSeed(Seed)
                .Derive(new KeyPath(path: $"m/{ServicePurpose}'/0'/0'/{chain}/{index}"))
                .VerifyMessage(data, signature);
        }

        public byte[] GetDeterministicSecret(Currency currency, DateTime timeStamp)
        {
            var utcTimeStamp = timeStamp.ToUniversalTime();

            var daysIndex = (int)(utcTimeStamp.Date - DateTime.MinValue).TotalDays;
            var secondsIndex = utcTimeStamp.Hour * 60 * 60 + utcTimeStamp.Minute * 60 + utcTimeStamp.Second;
            var msIndex = utcTimeStamp.Millisecond;

            var extKey = BitcoinBasedCurrency
                .CreateExtKeyFromSeed(Seed)
                .Derive(new KeyPath(path: $"m/{ServicePurpose}'/{currency.Bip44Code}'/0'/{daysIndex}/{secondsIndex}/{msIndex}"));

            extKey.GetPublicKey(out var publicKey);

            try
            {
                return Sha512.Compute(publicKey);
            }
            finally
            {
                publicKey.Clear();
            }  
        }

        public static HdKeyStorage LoadFromFile(string pathToFile, SecureString password)
        {
            if (!File.Exists(pathToFile))
                throw new FileNotFoundException($"File {pathToFile} not found.");

            if (new FileInfo(pathToFile).Length > MaxFileSizeInBytes)
                throw new Exception("File is too large");

            HdKeyStorage result;

            try
            {
                using (var stream = new FileStream(pathToFile, FileMode.Open))
                {
                    var network = stream.ReadByte();

                    var encryptedBytes = stream.ReadBytes((int)stream.Length - 1);

                    var decryptedBytes = Aes.Decrypt(
                        encryptedBytes: encryptedBytes,
                        password: password,
                        keySize: AesKeySize,
                        saltSize: AesSaltSize,
                        iterations: AesRfc2898Iterations);

                    var json = Encoding.UTF8.GetString(decryptedBytes);

                    result = JsonConvert.DeserializeObject<HdKeyStorage>(json);

                    if (result.Network != (Network)network)
                        throw new Exception("Wallet type does not match the type specified during creation");

                    if (result.Version != CurrentVersion)
                        throw new NotSupportedException($"Version {result.Version} does not match {CurrentVersion}");
                }
            }
            catch (Exception e)
            {
                Log.Error(e, "HdKeyStorage data loading error");
                throw;
            }

            return result;
        }

        public void SaveToFile(string pathToFile, SecureString password)
        {
            try
            {
                using (var stream = new FileStream(pathToFile, FileMode.Create))
                {
                    var serialized = JsonConvert.SerializeObject(this, Formatting.Indented);
                    var serializedBytes = Encoding.UTF8.GetBytes(serialized);

                    var encryptedBytes = Aes.Encrypt(
                        plainBytes: serializedBytes,
                        password: password,
                        keySize: AesKeySize,
                        saltSize: AesSaltSize,
                        iterations: AesRfc2898Iterations);

                    stream.WriteByte((byte)Network);
                    stream.Write(encryptedBytes, 0, encryptedBytes.Length);
                }
            }
            catch (Exception e)
            {
                Log.Error(e, "HdKeyStorage save to file error");
            }
        }
    }
}