using System;
using System.IO;
using System.Text;
using NBitcoin;
using Newtonsoft.Json;
using Serilog;

using Atomex.Common.Memory;
using Atomex.Cryptography;
using Atomex.Cryptography.Abstract;

namespace Atomex.Wallet.Settings
{
    public class UserSettings
    {
        private const int MaxFileSizeInBytes = 1 * 1024 * 1024; // 1  MB
        private const int AesKeySize = 256;
        private const int AesSaltSize = 16;
        private const int AesBlockSize = 128;

        public bool AutoSignOut { get; set; }
        public int PeriodOfInactivityInMin { get; set; }
        public uint AuthenticationKeyIndex { get; set; }
        public bool ShowActiveSwapWarning { get; set; }
        public int BalanceUpdateIntervalInSec { get; set; }

        public static UserSettings DefaultSettings => new UserSettings
        {
            AutoSignOut = true,
            PeriodOfInactivityInMin = 5,
            AuthenticationKeyIndex = 0,
            ShowActiveSwapWarning = true,
            BalanceUpdateIntervalInSec = 120
        };

        public UserSettings Clone()
        {
            return (UserSettings)MemberwiseClone();
        }

        public static UserSettings LoadFromFile(string pathToFile, SecureBytes keyPassword)
        {
            if (!File.Exists(pathToFile))
                return null;

            if (new FileInfo(pathToFile).Length > MaxFileSizeInBytes)
                return null;

            try
            {
                using var keyPw = keyPassword.ToUnmanagedBytes();

                using var keyUs = new UnmanagedBytes(64);
                MacAlgorithm.HmacSha512.Mac(keyPw, Encoding.UTF8.GetBytes("user settings encryption"), keyUs);

                using var aesKey = new UnmanagedBytes(keyUs.GetReadOnlySpan().Slice(0, AesKeySize / 8));
                using var aesIv = new UnmanagedBytes(keyUs.GetReadOnlySpan().Slice(AesKeySize / 8, AesBlockSize / 8));

                using var stream = new FileStream(pathToFile, FileMode.Open);

                var encryptedBytes = stream.ReadBytes((int)stream.Length);

                var decryptedBytes = Aes.Decrypt(
                    encryptedBytes: encryptedBytes,
                    key: aesKey.ToBytes(),
                    iv: aesIv.ToBytes(),
                    keySize: AesKeySize,
                    saltSize: AesSaltSize);

                var json = Encoding.UTF8.GetString(decryptedBytes);

                return JsonConvert.DeserializeObject<UserSettings>(json);
            }
            catch (Exception e)
            {
                Log.Error(e, "UserSettings loading error");

                throw e;
            }
        }

        public void SaveToFile(string pathToFile, SecureBytes keyPassword)
        {
            try
            {
                using var keyPw = keyPassword.ToUnmanagedBytes();

                using var keyUs = new UnmanagedBytes(64);
                MacAlgorithm.HmacSha512.Mac(keyPw, Encoding.UTF8.GetBytes("user settings encryption"), keyUs);

                var aesSalt = Rand.SecureRandomBytes(AesSaltSize);
                using var aesKey = new UnmanagedBytes(keyUs.GetReadOnlySpan().Slice(0, AesKeySize / 8));
                using var aesIv = new UnmanagedBytes(keyUs.GetReadOnlySpan().Slice(AesKeySize / 8, AesBlockSize / 8));

                var serialized = JsonConvert.SerializeObject(this, Formatting.Indented);
                var serializedBytes = Encoding.UTF8.GetBytes(serialized);

                var encryptedBytes = Aes.Encrypt(
                    plainBytes: serializedBytes,
                    salt: aesSalt,
                    key: aesKey.ToBytes(),
                    iv: aesIv.ToBytes(),
                    keySize: AesKeySize);

                using var stream = new FileStream(pathToFile, FileMode.Create);
                stream.Write(encryptedBytes, 0, encryptedBytes.Length);
            }
            catch (Exception e)
            {
                Log.Error(e, "UserSettings save to file error");
            }
        }
    }
}