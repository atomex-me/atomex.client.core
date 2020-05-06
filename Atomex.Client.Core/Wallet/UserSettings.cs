using System;
using System.IO;
using System.Security;
using System.Text;
using Atomex.Cryptography;
using NBitcoin;
using Newtonsoft.Json;
using Serilog;

namespace Atomex.Wallet
{
    public class UserSettings
    {
        private const int MaxFileSizeInBytes = 1 * 1024 * 1024; // 1  MB
        private const int AesKeySize = 256;
        private const int AesSaltSize = 16;
        private const int AesRfc2898Iterations = 1024;

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

        public static UserSettings TryLoadFromFile(string pathToFile, SecureString password)
        {
            if (!File.Exists(pathToFile))
                return null;

            if (new FileInfo(pathToFile).Length > MaxFileSizeInBytes)
                return null;

            try
            {
                using (var stream = new FileStream(pathToFile, FileMode.Open))
                {
                    var encryptedBytes = stream.ReadBytes((int) stream.Length);

                    var decryptedBytes = Aes.Decrypt(
                        encryptedBytes: encryptedBytes,
                        password: password,
                        keySize: AesKeySize,
                        saltSize: AesSaltSize,
                        iterations: AesRfc2898Iterations);

                    var json = Encoding.UTF8.GetString(decryptedBytes);

                    return JsonConvert.DeserializeObject<UserSettings>(json);
                }
            }
            catch (Exception e)
            {
                Console.WriteLine("UserSettings loading error");
            }

            return null;
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

                    stream.Write(encryptedBytes, 0, encryptedBytes.Length);
                }
            }
            catch (Exception e)
            {
                Console.WriteLine("UserSettings save to file error");
            }
        }
    }
}