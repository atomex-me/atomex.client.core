using System;
using System.IO;
using System.Security;
using System.Text;
using System.Threading.Tasks;

using NBitcoin;
using Newtonsoft.Json;
using Serilog;

using Atomex.Common;
using Atomex.Common.Memory;
using Atomex.Core;
using Atomex.Cryptography.Abstract;
using Atomex.Cryptography.DotNet;
using Atomex.Wallets;
using Atomex.Wallet.Abstract;
using Atomex.Wallet.Bip;
using Network = Atomex.Core.Network;

namespace Atomex.Wallet
{
    public class HdKeyStorage : IKeyStorage
    {
        public const string CurrentVersion = "1.0.0.0";
        private const int ServicePurpose = 777;
        private const int MaxFileSizeInBytes = 100 * 1024 * 1024; // 100 Mb

        private const int AesKeySize = 256;
        private const int AesSaltSize = 16;
        private const int AesRfc2898Iterations = 1024;

        public Network Network { get; set; }
        public string EncryptedSeed { get; set; }
        public string Version { get; set; }

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

            var scopedSeed = new Mnemonic(mnemonic, wordList)
                .DeriveSeed(passPhrase.ToUnsecuredString());

            Seed = new SecureBytes(scopedSeed);
        }

        public void Lock()
        {
            Seed.Dispose();
            Seed = null;
        }

        public HdKeyStorage Unlock(SecureString password)
        {
            try
            {
                var seed = new AesCbc(
                        keySize: AesKeySize,
                        saltSize: AesSaltSize,
                        iterations: AesRfc2898Iterations)
                    .Decrypt(
                        key: password.ToBytes(),
                        ciphertext: Hex.FromString(EncryptedSeed));

                Seed = new SecureBytes(seed);
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
                var scopedSeed = Seed.ToUnsecuredBytes();

                EncryptedSeed = new AesCbc(
                        keySize: AesKeySize,
                        saltSize: AesSaltSize,
                        iterations: AesRfc2898Iterations)
                    .Encrypt(
                        key: password.ToBytes(),
                        plaintext: scopedSeed)
                    .ToHexString();
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
            CurrencyConfig currency,
            int purpose,
            uint account,
            uint chain,
            uint index,
            int keyType)
        {
            using var masterKey = currency.CreateExtKey(Seed, keyType);

            if (keyType == CurrencyConfig.StandardKey && Currencies.IsTezosBased(currency.Name))
            {
                return masterKey.Derive($"m/{purpose}'/{currency.Bip44Code}'/{account}'/{chain}'");
            }

            return masterKey.Derive($"m/{purpose}'/{currency.Bip44Code}'/{account}'/{chain}/{index}");
        }

        private IExtKey GetExtKey(
            CurrencyConfig currency,
            int purpose,
            KeyIndex keyIndex,
            int keyType)
        {
            return GetExtKey(
                currency: currency,
                purpose: purpose,
                account: keyIndex.Account,
                chain: keyIndex.Chain,
                index: keyIndex.Index,
                keyType: keyType);
        }

        public SecureBytes GetPublicKey(
            CurrencyConfig currency,
            KeyIndex keyIndex,
            int keyType)
        {
            return GetPublicKey(
                currency: currency,
                account: keyIndex.Account,
                chain: keyIndex.Chain,
                index: keyIndex.Index,
                keyType: keyType);
        }

        public SecureBytes GetPublicKey(
            CurrencyConfig currency,
            uint account,
            uint chain,
            uint index,
            int keyType)
        {
            var purpose = keyType == BitcoinBasedConfig.SegwitKey && Currencies.IsBitcoinBased(currency.Name)
                ? Bip84.Purpose
                : Bip44.Purpose;

            using var extKey = GetExtKey(
                currency: currency,
                purpose: purpose,
                account: account,
                chain: chain,
                index: index,
                keyType: keyType);

            return extKey.GetPublicKey();
        }

        public SecureBytes GetServicePublicKey(uint index)
        {
            using var masterKey = BitcoinBasedConfig
                .CreateExtKeyFromSeed(Seed);

            using var extKey = masterKey
                .Derive($"m/{ServicePurpose}'/0'/0'/0/{index}");

            return extKey.GetPublicKey();
        }

        public SecureBytes GetPrivateKey(
            CurrencyConfig currency,
            KeyIndex keyIndex,
            int keyType)
        {
            var purpose = keyType == BitcoinBasedConfig.SegwitKey && Currencies.IsBitcoinBased(currency.Name)
                ? Bip84.Purpose
                : Bip44.Purpose;

            using var extKey = GetExtKey(
                currency: currency,
                purpose: purpose,
                keyIndex: keyIndex,
                keyType: keyType);

            return extKey.GetPrivateKey();
        }

        public byte[] SignHash(
            CurrencyConfig currency,
            byte[] hash,
            KeyIndex keyIndex,
            int keyType)
        {
            var purpose = keyType == BitcoinBasedConfig.SegwitKey && Currencies.IsBitcoinBased(currency.Name)
                ? Bip84.Purpose
                : Bip44.Purpose;

            using var extKey = GetExtKey(
                currency: currency,
                purpose: purpose,
                keyIndex: keyIndex,
                keyType: keyType);

            return extKey.Sign(hash);
        }

        public byte[] SignByServiceKey(byte[] data, int chain, uint index)
        {
            using var masterKey = BitcoinBasedConfig
                .CreateExtKeyFromSeed(Seed);

            using var derivedKey = masterKey
                .Derive($"m/{ServicePurpose}'/0'/0'/{chain}/{index}");

            return derivedKey.Sign(data);
        }

        public bool VerifyHash(
            CurrencyConfig currency,
            byte[] hash,
            byte[] signature,
            KeyIndex keyIndex,
            int keyType)
        {
            var purpose = keyType == BitcoinBasedConfig.SegwitKey && Currencies.IsBitcoinBased(currency.Name)
                ? Bip84.Purpose
                : Bip44.Purpose;

            using var extKey = GetExtKey(
                currency: currency,
                purpose: purpose,
                keyIndex: keyIndex,
                keyType: keyType);

            return extKey.Verify(hash, signature);
        }

        public bool VerifyByServiceKey(
            byte[] data,
            byte[] signature,
            int chain,
            uint index)
        {
            using var masterKey = BitcoinBasedConfig
                .CreateExtKeyFromSeed(Seed);

            using var derivedKey = masterKey
                .Derive($"m/{ServicePurpose}'/0'/0'/{chain}/{index}");

            return derivedKey.Verify(data, signature);
        }

        private static readonly object _secretCounterSync = new();
        private static int _secretCounter = 0;
        private static long _secretTimeStampMs = 0;
        public static int SecretCounter(long timeStamp)
        {
            lock (_secretCounterSync)
            {
                if (_secretTimeStampMs != timeStamp)
                {
                    _secretCounter = 0;
                    _secretTimeStampMs = timeStamp;
                }

                return _secretCounter++;
            }
        }

        public byte[] GetDeterministicSecret(CurrencyConfig currency, DateTime timeStamp)
        {
            var utcTimeStamp = timeStamp.ToUniversalTime();

            var daysIndex = (int)(utcTimeStamp.Date - DateTime.MinValue).TotalDays;
            var secondsIndex = (utcTimeStamp.Hour * 60 * 60) + (utcTimeStamp.Minute * 60) + utcTimeStamp.Second;
            var msIndex = utcTimeStamp.Millisecond;

            var unixTimeMs = utcTimeStamp.ToUnixTimeMs();
            var counter = SecretCounter(unixTimeMs);

            using var masterKey = BitcoinBasedConfig
                .CreateExtKeyFromSeed(Seed);

            using var extKey = masterKey
                .Derive($"m/{ServicePurpose}'/{currency.Bip44Code}'/0'/{daysIndex}/{secondsIndex}/{msIndex}/{counter}");

            using var securePublicKey = extKey.GetPublicKey();
            var publicKey = securePublicKey.ToUnsecuredBytes();

            return HashAlgorithm.Sha512.Hash(publicKey);
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

                var decryptedBytes = new AesCbc(
                        keySize: AesKeySize,
                        saltSize: AesSaltSize,
                        iterations: AesRfc2898Iterations)
                    .Decrypt(
                        key: password.ToBytes(),
                        ciphertext: encryptedBytes);

                var json = Encoding.UTF8.GetString(decryptedBytes);

                result = JsonConvert.DeserializeObject<HdKeyStorage>(json);

                if (result.Network != (Network)network)
                    throw new Exception("Wallet type does not match the type specified during creation");

                if (result.Version != CurrentVersion)
                    throw new NotSupportedException($"Version {result.Version} does not match {CurrentVersion}");
            }
            catch (Exception e)
            {
                Log.Error(e, "HdKeyStorage data loading error");
                throw;
            }

            return result;
        }

        public bool SaveToFile(string pathToFile, SecureString password)
        {
            try
            {
                using var stream = new FileStream(pathToFile, FileMode.Create);
                var serialized = JsonConvert.SerializeObject(this, Formatting.Indented);
                var serializedBytes = Encoding.UTF8.GetBytes(serialized);

                var encryptedBytes = new AesCbc(
                        keySize: AesKeySize,
                        saltSize: AesSaltSize,
                        iterations: AesRfc2898Iterations)
                    .Encrypt(
                        key: password.ToBytes(),
                        plaintext: serializedBytes);

                stream.WriteByte((byte)Network);
                stream.Write(encryptedBytes, 0, encryptedBytes.Length);
            }
            catch (Exception e)
            {
                Log.Error(e, "HdKeyStorage save to file error");

                return false;
            }

            return true;
        }
    }
}