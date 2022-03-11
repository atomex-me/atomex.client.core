using System;
using System.IO;
using System.Linq;
using System.Text;
using Atomex.Abstract;
using Newtonsoft.Json;
using Serilog;

namespace Atomex.Wallet
{
    public class UserSettings
    {
        private const int MaxFileSizeInBytes = 1 * 1024 * 1024; // 1  MB
        // private const int AesKeySize = 256;
        // private const int AesSaltSize = 16;
        // private const int AesRfc2898Iterations = 1024;

        public bool AutoSignOut { get; set; }
        public int PeriodOfInactivityInMin { get; set; }
        public uint AuthenticationKeyIndex { get; set; }
        public bool ShowActiveSwapWarning { get; set; }
        public int BalanceUpdateIntervalInSec { get; set; }
        public string[] InitializedCurrencies { get; set; }

        public static UserSettings GetDefaultSettings(ICurrencies currencies)
        {
            return new UserSettings
            {
                AutoSignOut = true,
                PeriodOfInactivityInMin = 5,
                AuthenticationKeyIndex = 0,
                ShowActiveSwapWarning = true,
                BalanceUpdateIntervalInSec = 120,
                InitializedCurrencies = currencies.Select(curr => curr.Name).ToArray()
            };
        }

        public UserSettings Clone()
        {
            return (UserSettings)MemberwiseClone();
        }

        public static UserSettings TryLoadFromFile(string pathToFile)
        {
            if (!File.Exists(pathToFile))
                return null;

            if (new FileInfo(pathToFile).Length > MaxFileSizeInBytes)
                return null;

            try
            {
                var json = File.ReadAllText(pathToFile);
                return JsonConvert.DeserializeObject<UserSettings>(json);
            }
            catch (Exception e)
            {
                Log.Error(e, "UserSettings loading error");
            }

            return null;
        }

        public void SaveToFile(string pathToFile)
        {
            try
            {
                var serialized = JsonConvert.SerializeObject(this, Formatting.Indented);
                File.WriteAllText(pathToFile, serialized, Encoding.UTF8);
            }
            catch (Exception e)
            {
                Log.Error(e, "UserSettings save to file error");
            }
        }
    }
}