using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Atomex.Abstract;
using Newtonsoft.Json;
using Serilog;

namespace Atomex.Wallet
{
    public enum AtomexNotificationType
    {
        Income,
        Outcome,
        Swap,
        Info
    }

    public class AtomexNotification
    {
        public string Id { get; set; }
        public string Message { get; set; }
        public DateTime Time { get; set; }
        public bool IsRead { get; set; }
        public AtomexNotificationType AtomexNotificationType { get; set; }   
    }

    public class UserData
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
        public string[] DisabledCurrencies { get; set; }
        public string[] DisabledTokens { get; set; }
        public string[] DisabledCollectibles { get; set; }
        public bool? HideTokensWithLowBalance { get; set; }
        public List<AtomexNotification> Notifications { get; set; }

        public static UserData GetDefaultSettings(ICurrencies currencies)
        {
            return new UserData
            {
                AutoSignOut = true,
                PeriodOfInactivityInMin = 5,
                AuthenticationKeyIndex = 0,
                ShowActiveSwapWarning = true,
                BalanceUpdateIntervalInSec = 120,
                DisabledCurrencies = Array.Empty<string>(),
                Notifications = new List<AtomexNotification>()
            };
        }

        public UserData Clone()
        {
            return (UserData)MemberwiseClone();
        }

        public static UserData TryLoadFromFile(string pathToFile)
        {
            if (!File.Exists(pathToFile))
                return null;

            if (new FileInfo(pathToFile).Length > MaxFileSizeInBytes)
                return null;

            try
            {
                var json = File.ReadAllText(pathToFile);
                return JsonConvert.DeserializeObject<UserData>(json);
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