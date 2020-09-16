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
using Atomex.Cryptography;
using Atomex.Cryptography.Abstract;
using Atomex.Wallet.Abstract;
using Atomex.Wallet.Bips;
using Aes = Atomex.Cryptography.Aes;
using Network = Atomex.Core.Network;

namespace Atomex.Wallet.KeyStorage
{
    public class HdKeyStorage : IKeyStorage
    {
        public const string CurrentVersion = "2.0.0.0";
        public const int NonHdKeysChain = -1;
        private const int ServicePurpose = 777;
        private const int MaxFileSizeInBytes = 100 * 1024 * 1024; // 100 Mb

        private const int AesKeySize = 256;
        private const int AesSaltSize = 16;
        private const int AesBlockSize = 128;

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
            SecureBytes seed,
            Network network = Network.MainNet)
        {
            Version = CurrentVersion;
            Network = network;
            Seed = seed;
        }

        public HdKeyStorage(
            string mnemonic,
            Wordlist wordList = null,
            SecureString passPhrase = null,
            Network network = Network.MainNet)
        {
            Version = CurrentVersion;
            Network = network;

            Seed = new SecureBytes(new Mnemonic(mnemonic, wordList)
                .DeriveSeed(passPhrase.ToUnsecuredString()));
        }

        public void Lock()
        {
            Seed.Dispose();
            Seed = null;
        }

        public HdKeyStorage Unlock(SecureBytes keyPassword)
        {
            try
            {
                using var keyPw = keyPassword.ToUnmanagedBytes();

                using var keyKs = new UnmanagedBytes(64);
                MacAlgorithm.HmacSha512.Mac(keyPw, Encoding.UTF8.GetBytes("seed encryption"), keyKs);

                using var aesKey = new UnmanagedBytes(keyKs.GetReadOnlySpan().Slice(0, AesKeySize / 8));
                using var aesIv = new UnmanagedBytes(keyKs.GetReadOnlySpan().Slice(AesKeySize / 8, AesBlockSize / 8));

                Seed = new SecureBytes(Aes.Decrypt(
                    encryptedBytes: Hex.FromString(EncryptedSeed),
                    key: aesKey.ToBytes(),
                    iv: aesIv.ToBytes(),
                    keySize: AesKeySize,
                    saltSize: AesSaltSize));
            }
            catch (Exception e)
            {
                Log.Error(e, "Unlock error");
            }

            return this;
        }

        public void Encrypt(SecureBytes keyPassword)
        {
            try
            {
                using var keyPw = keyPassword.ToUnmanagedBytes();

                using var keyKs = new UnmanagedBytes(64);
                MacAlgorithm.HmacSha512.Mac(keyPw, Encoding.UTF8.GetBytes("seed encryption"), keyKs);

                var aesSalt = Rand.SecureRandomBytes(AesSaltSize);
                using var aesKey = new UnmanagedBytes(keyKs.GetReadOnlySpan().Slice(0, AesKeySize / 8));
                using var aesIv = new UnmanagedBytes(keyKs.GetReadOnlySpan().Slice(AesKeySize / 8, AesBlockSize / 8));

                var scopedSeed = Seed.ToUnsecuredBytes();

                EncryptedSeed = Aes.Encrypt(
                        plainBytes: scopedSeed,
                        salt: aesSalt,
                        key: aesKey.ToBytes(),
                        iv: aesIv.ToBytes(),
                        keySize: AesKeySize)
                    .ToHexString();
            }
            catch (Exception e)
            {
                Log.Error(e, "Encrypt error");
            }
        }

        public Task EncryptAsync(SecureBytes keyPassword)
        {
            return Task.Factory.StartNew(() => Encrypt(keyPassword));
        }

        private IExtKey GetExtKey(
            Currency currency,
            int purpose,
            int chain,
            uint index)
        {
            using var masterKey = currency.CreateExtKey(Seed);

            return masterKey.Derive($"m/{purpose}'/{currency.Bip44Code}'/0'/{chain}/{index}");
        }

        public SecureBytes GetPublicKey(Currency currency, KeyIndex keyIndex)
        {
            return GetPublicKey(currency, keyIndex.Chain, keyIndex.Index);
        }

        public SecureBytes GetPublicKey(Currency currency, int chain, uint index)
        {
            using var extKey = GetExtKey(
                currency: currency,
                purpose: Bip44.Purpose,
                chain: chain,
                index: index);

            return extKey.GetPublicKey();
        }

        public SecureBytes GetServicePublicKey(uint index)
        {
            using var masterKey = BitcoinBasedCurrency
                .CreateExtKeyFromSeed(Seed);

            using var extKey = masterKey
                .Derive($"m/{ServicePurpose}'/0'/0'/0/{index}");

            return extKey.GetPublicKey();
        }

        public SecureBytes GetPrivateKey(Currency currency, KeyIndex keyIndex)
        {
            using var extKey = GetExtKey(
                currency: currency,
                purpose: Bip44.Purpose,
                chain: keyIndex.Chain,
                index: keyIndex.Index);

            return extKey.GetPrivateKey();
        }

        public byte[] SignHash(Currency currency, byte[] hash, KeyIndex keyIndex)
        {
            using var extKey = GetExtKey(
                currency: currency,
                purpose: Bip44.Purpose,
                chain: keyIndex.Chain,
                index: keyIndex.Index);

            return extKey.SignHash(hash);
        }

        public byte[] SignMessage(Currency currency, byte[] data, KeyIndex keyIndex)
        {
            using var extKey = GetExtKey(
                currency: currency,
                purpose: Bip44.Purpose,
                chain: keyIndex.Chain,
                index: keyIndex.Index);

            return extKey.SignHash(data);
        }

        public byte[] SignMessageByServiceKey(byte[] data, int chain, uint index)
        {
            using var masterKey = BitcoinBasedCurrency
                .CreateExtKeyFromSeed(Seed);

            using var derivedKey = masterKey
                .Derive($"m/{ServicePurpose}'/0'/0'/{chain}/{index}");

            using var hash = new UnmanagedBytes(32);

            HashAlgorithm.Sha256.Hash(data, hash);

            return derivedKey.SignHash(hash);
        }

        public bool VerifyHash(
            Currency currency,
            byte[] hash,
            byte[] signature,
            KeyIndex keyIndex)
        {
            using var extKey = GetExtKey(
                currency: currency,
                purpose: Bip44.Purpose,
                chain: keyIndex.Chain,
                index: keyIndex.Index);

            return extKey.VerifyHash(hash, signature);
        }

        public bool VerifyMessage(
            Currency currency,
            byte[] data,
            byte[] signature,
            KeyIndex keyIndex)
        {
            using var extKey = GetExtKey(
                currency: currency,
                purpose: Bip44.Purpose,
                chain: keyIndex.Chain,
                index: keyIndex.Index);

            return extKey.VerifyHash(data, signature);
        }

        public bool VerifyMessageByServiceKey(
            byte[] data,
            byte[] signature,
            int chain,
            uint index)
        {
            using var masterKey = BitcoinBasedCurrency
                .CreateExtKeyFromSeed(Seed);

            using var derivedKey = masterKey
                .Derive($"m/{ServicePurpose}'/0'/0'/{chain}/{index}");

            using var hash = new UnmanagedBytes(32);

            HashAlgorithm.Sha256.Hash(data, hash);

            return derivedKey.VerifyHash(hash, signature);
        }

        public byte[] GetDeterministicSecret(Currency currency, DateTime timeStamp)
        {
            var utcTimeStamp = timeStamp.ToUniversalTime();

            var daysIndex = (int)(utcTimeStamp.Date - DateTime.MinValue).TotalDays;
            var secondsIndex = utcTimeStamp.Hour * 60 * 60 + utcTimeStamp.Minute * 60 + utcTimeStamp.Second;
            var msIndex = utcTimeStamp.Millisecond;

            using var masterKey = BitcoinBasedCurrency
                .CreateExtKeyFromSeed(Seed);

            using var extKey = masterKey
                .Derive($"m/{ServicePurpose}'/{currency.Bip44Code}'/0'/{daysIndex}/{secondsIndex}/{msIndex}");

            using var securePublicKey = extKey.GetPublicKey();
            using var publicKey = securePublicKey.ToUnmanagedBytes();

            return HashAlgorithm.Sha512.Hash(publicKey);
        }

        public static byte[] ReadSalt(string pathToFile)
        {
            if (!File.Exists(pathToFile))
                throw new FileNotFoundException($"File {pathToFile} not found.");

            using var stream = new FileStream(pathToFile, FileMode.Open);
            var _ = stream.ReadByte();

            const int saltSize = 16;
            return stream.ReadBytes(saltSize);
        }

        public static HdKeyStorage LoadFromFile(string pathToFile, SecureBytes keyPassword)
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

                const int saltSize = 16;
                var _ = stream.ReadBytes(saltSize);

                using var keyPw = keyPassword.ToUnmanagedBytes();

                using var keyKs = new UnmanagedBytes(64);
                MacAlgorithm.HmacSha512.Mac(keyPw, Encoding.UTF8.GetBytes("key storage encryption"), keyKs);

                using var aesKey = new UnmanagedBytes(keyKs.GetReadOnlySpan().Slice(0, AesKeySize / 8));
                using var aesIv = new UnmanagedBytes(keyKs.GetReadOnlySpan().Slice(AesKeySize / 8, AesBlockSize / 8));

                var encryptedBytes = stream.ReadBytes((int)stream.Length - saltSize - 1);

                var decryptedBytes = Aes.Decrypt(
                    encryptedBytes: encryptedBytes,
                    key: aesKey.ToBytes(),
                    iv: aesIv.ToBytes(),
                    keySize: AesKeySize,
                    saltSize: AesSaltSize);

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

        public void SaveToFile(string pathToFile, SecureBytes keyPassword, byte[] salt)
        {
            try
            {
                using var keyPw = keyPassword.ToUnmanagedBytes();

                using var keyKs = new UnmanagedBytes(64);
                MacAlgorithm.HmacSha512.Mac(keyPw, Encoding.UTF8.GetBytes("key storage encryption"), keyKs);

                var aesSalt = Rand.SecureRandomBytes(AesSaltSize);
                using var aesKey = new UnmanagedBytes(keyKs.GetReadOnlySpan().Slice(0, AesKeySize / 8));
                using var aesIv = new UnmanagedBytes(keyKs.GetReadOnlySpan().Slice(AesKeySize / 8, AesBlockSize / 8));

                var serialized = JsonConvert.SerializeObject(this, Formatting.Indented);
                var serializedBytes = Encoding.UTF8.GetBytes(serialized);

                var encryptedBytes = Aes.Encrypt(
                    plainBytes: serializedBytes,
                    salt: aesSalt,
                    key: aesKey.ToBytes(),
                    iv: aesIv.ToBytes(),
                    keySize: AesKeySize);

                using var stream = new FileStream(pathToFile, FileMode.Create);
                stream.WriteByte((byte)Network);
                stream.Write(salt, 0, salt.Length);
                stream.Write(encryptedBytes, 0, encryptedBytes.Length);
            }
            catch (Exception e)
            {
                Log.Error(e, "HdKeyStorage save to file error");
            }
        }
    }
}