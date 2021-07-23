using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security;
using System.Text;
using System.Threading.Tasks;
using Atomex.Common;
using Atomex.Core;
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
        public SecureBytes Seed { get; set; }

        public void Lock()
        {
            Seed.Dispose();
            Seed = null;
        }

        public void Unlock(SecureString password)
        {
            try
            {
                using var scopedSeed = new ScopedBytes(Aes.Decrypt(
                    encryptedBytes: Hex.FromString(EncryptedSeed),
                    password: password,
                    keySize: AesKeySize,
                    saltSize: AesSaltSize,
                    iterations: AesRfc2898Iterations));

                Seed = new SecureBytes(scopedSeed);
            }
            catch (Exception e)
            {
                Log.Error("Unlock error");
            }
        }

        public void Encrypt(SecureString password)
        {
            try
            {
                using var scopedSeed = Seed.ToUnsecuredBytes();

                EncryptedSeed = Aes.Encrypt(
                        plainBytes: scopedSeed,
                        password: password,
                        keySize: AesKeySize,
                        saltSize: AesSaltSize,
                        iterations: AesRfc2898Iterations)
                    .ToHexString();
            }
            catch (Exception e)
            {
                Log.Error("Encrypt error");
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
        private SecureBytes Seed { get; set; }

        [JsonIgnore]
        public bool IsLocked => Seed == null;

        public HdKeyStorage()
        {
        }

        public HdKeyStorage(
            string mnemonic,
            Wordlist wordList = null,
            SecureString passPhrase = null,
            Network network = Network.MainNet)
        {
            Version = CurrentVersion;
            Network = network;

            using var scopedSeed = new ScopedBytes(new Mnemonic(mnemonic, wordList)
                .DeriveSeed(passPhrase.ToUnsecuredString()));

            Seed = new SecureBytes(scopedSeed);
        }

        public void Lock()
        {
            Seed.Dispose();
            Seed = null;

            foreach (var singleKey in NonHdKeys)
                singleKey.Lock();
        }

        public HdKeyStorage Unlock(SecureString password)
        {
            try
            {
                using var scopedSeed = new ScopedBytes(Aes.Decrypt(
                    encryptedBytes: Hex.FromString(EncryptedSeed),
                    password: password,
                    keySize: AesKeySize,
                    saltSize: AesSaltSize,
                    iterations: AesRfc2898Iterations));

                Seed = new SecureBytes(scopedSeed);

                foreach (var singleKey in NonHdKeys)
                    singleKey.Unlock(password);
            }
            catch (Exception e)
            {
                Log.Error("Unlock error");
            }

            return this;
        }

        public void Encrypt(SecureString password)
        {
            try
            {
                using var scopedSeed = Seed.ToUnsecuredBytes();

                EncryptedSeed = Aes.Encrypt(
                        plainBytes: scopedSeed,
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
                Log.Error("Encrypt error");
            }
        }

        public Task EncryptAsync(SecureString password)
        {
            return Task.Factory.StartNew(() => Encrypt(password));
        }

        private IExtKey GetExtKey(
            CurrencyConfig currency,
            int purpose,
            int chain,
            uint index)
        {
            using var masterKey = currency.CreateExtKey(Seed);
            
            return masterKey.Derive(new KeyPath(path: $"m/{purpose}'/{currency.Bip44Code}'/0'/{chain}/{index}"));               
        }

        private IKey GetNonHdKey(CurrencyConfig currency, uint index)
        {
            var nonHdKeys = NonHdKeys
                .Where(s => s.CurrencyCode == currency.Bip44Code)
                .ToList();

            return index < nonHdKeys.Count
                ? currency.CreateKey(nonHdKeys[(int)index].Seed)
                : null;
        }

        public SecureBytes GetPublicKey(CurrencyConfig currency, KeyIndex keyIndex)
        {
            return GetPublicKey(currency, keyIndex.Chain, keyIndex.Index);
        }

        public SecureBytes GetPublicKey(CurrencyConfig currency, int chain, uint index)
        {
            if (chain == NonHdKeysChain)
                return GetNonHdPublicKey(currency, index);

            using var extKey = GetExtKey(
                currency: currency,
                purpose: Bip44.Purpose,
                chain: chain,
                index: index);

            return extKey.GetPublicKey();
        }

        private SecureBytes GetNonHdPublicKey(CurrencyConfig currency, uint index)
        {
            using var key = GetNonHdKey(currency, index);

            if (key == null)
                return null;

            return key.GetPublicKey();
        }

        public SecureBytes GetServicePublicKey(uint index)
        {
            using var masterKey = BitcoinBasedConfig
                .CreateExtKeyFromSeed(Seed);

            using var extKey = masterKey
                .Derive(new KeyPath(path: $"m/{ServicePurpose}'/0'/0'/0/{index}"));

            return extKey.GetPublicKey();
        }

        public SecureBytes GetPrivateKey(CurrencyConfig currency, KeyIndex keyIndex)
        {
            if (keyIndex.Chain == NonHdKeysChain)
                return GetNonHdPrivateKey(currency, keyIndex.Index);

            using var extKey = GetExtKey(
                currency: currency,
                purpose: Bip44.Purpose,
                chain: keyIndex.Chain,
                index: keyIndex.Index);

            return extKey.GetPrivateKey();
        }

        private SecureBytes GetNonHdPrivateKey(CurrencyConfig currency, uint index)
        {
            using var key = GetNonHdKey(currency, index);

            if (key == null)
                return null;

            return key.GetPrivateKey();
        }

        public byte[] SignHash(CurrencyConfig currency, byte[] hash, KeyIndex keyIndex)
        {
            if (keyIndex.Chain == NonHdKeysChain)
            {
                using var nonHdKey = GetNonHdKey(currency, keyIndex.Index);

                return nonHdKey.SignHash(hash);
            }

            using var extKey = GetExtKey(
                currency: currency,
                purpose: Bip44.Purpose,
                chain: keyIndex.Chain,
                index: keyIndex.Index);

            return extKey.SignHash(hash);
        }

        public byte[] SignMessage(CurrencyConfig currency, byte[] data, KeyIndex keyIndex)
        {
            if (keyIndex.Chain == NonHdKeysChain)
            {
                using var nonHdKey = GetNonHdKey(currency, keyIndex.Index);

                return nonHdKey.SignMessage(data);
            }

            using var extKey = GetExtKey(
                currency: currency,
                purpose: Bip44.Purpose,
                chain: keyIndex.Chain,
                index: keyIndex.Index);

            return extKey.SignMessage(data);
        }

        public byte[] SignMessageByServiceKey(byte[] data, int chain, uint index)
        {
            using var masterKey = BitcoinBasedConfig
                .CreateExtKeyFromSeed(Seed);

            using var derivedKey = masterKey
                .Derive(new KeyPath(path: $"m/{ServicePurpose}'/0'/0'/{chain}/{index}"));

            return derivedKey.SignMessage(data);
        }

        public bool VerifyHash(
            CurrencyConfig currency,
            byte[] hash,
            byte[] signature,
            KeyIndex keyIndex)
        {
            if (keyIndex.Chain == NonHdKeysChain)
            {
                using var nonHdKey = GetNonHdKey(currency, keyIndex.Index);

                return nonHdKey.VerifyHash(hash, signature);
            }

            using var extKey = GetExtKey(
                currency: currency,
                purpose: Bip44.Purpose,
                chain: keyIndex.Chain,
                index: keyIndex.Index);

            return extKey.VerifyHash(hash, signature);
        }

        public bool VerifyMessage(
            CurrencyConfig currency,
            byte[] data,
            byte[] signature,
            KeyIndex keyIndex)
        {
            if (keyIndex.Chain == NonHdKeysChain)
            {
                using var nonHdKey = GetNonHdKey(currency, keyIndex.Index);

                return nonHdKey.VerifyMessage(data, signature);
            }

            using var extKey = GetExtKey(
                currency: currency,
                purpose: Bip44.Purpose,
                chain: keyIndex.Chain,
                index: keyIndex.Index);

            return extKey.VerifyMessage(data, signature);
        }

        public bool VerifyMessageByServiceKey(
            byte[] data,
            byte[] signature,
            int chain,
            uint index)
        {
            using var masterKey = BitcoinBasedConfig
                .CreateExtKeyFromSeed(Seed);

            using var derivedKey = masterKey
                .Derive(new KeyPath(path: $"m/{ServicePurpose}'/0'/0'/{chain}/{index}"));

            return derivedKey.VerifyMessage(data, signature);
        }

        public byte[] GetDeterministicSecret(CurrencyConfig currency, DateTime timeStamp)
        {
            var utcTimeStamp = timeStamp.ToUniversalTime();

            var daysIndex = (int)(utcTimeStamp.Date - DateTime.MinValue).TotalDays;
            var secondsIndex = utcTimeStamp.Hour * 60 * 60 + utcTimeStamp.Minute * 60 + utcTimeStamp.Second;
            var msIndex = utcTimeStamp.Millisecond;

            using var masterKey = BitcoinBasedConfig
                .CreateExtKeyFromSeed(Seed);

            using var extKey = masterKey
                .Derive(new KeyPath(path: $"m/{ServicePurpose}'/{currency.Bip44Code}'/0'/{daysIndex}/{secondsIndex}/{msIndex}"));

            using var securePublicKey = extKey.GetPublicKey();
            using var publicKey = securePublicKey.ToUnsecuredBytes();

            return Sha512.Compute(publicKey);
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
                using var stream = new FileStream(pathToFile, FileMode.Open);
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
            catch (Exception e)
            {
                Log.Warning("HdKeyStorage data loading error");
                throw;
            }

            return result;
        }

        public void SaveToFile(string pathToFile, SecureString password)
        {
            try
            {
                using var stream = new FileStream(pathToFile, FileMode.Create);
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
            catch (Exception e)
            {
                Log.Error("HdKeyStorage save to file error");
            }
        }
    }
}