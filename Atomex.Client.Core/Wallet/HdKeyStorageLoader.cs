using System;
using System.Collections.Generic;
using System.IO;
using System.Security;

namespace Atomex.Wallet
{
    public static class HdKeyStorageLoader
    {
        private class StorageVersion
        {
            public Version Version { get; set; }
            public Func<string, SecureString, object> LoadFromFile { get; set; }
            public Func<object, object> Up { get; set; }
        }

        private static IList<StorageVersion> Versions { get; } = new List<StorageVersion>
        {
            new StorageVersion
            {
                Version = new Version(version: "1.0.0.0"),
                LoadFromFile = HdKeyStorage.LoadFromFile,
                Up = keyStorage => keyStorage
            }
        };

        public static HdKeyStorage LoadFromFile(
            string pathToFile,
            SecureString password)
        {
            if (!File.Exists(pathToFile))
                throw new FileNotFoundException($"File {pathToFile} not found.");

            object keyStorage = null;
            StorageVersion storageVersion = null;
            Exception loadingException = null;

            foreach (var version in Versions)
            {
                storageVersion = version;

                try
                {
                    keyStorage = version.LoadFromFile(pathToFile, password);
                    loadingException = null;
                    break;
                }
                catch (Exception e)
                {
                    loadingException = e;
                }
            }

            if (keyStorage == null)
                throw loadingException ?? throw new Exception("Key storage loading error");

            var versionIndex = Versions.IndexOf(storageVersion);

            // apply migrations
            for (var i = versionIndex; i >= 0; --i)
                keyStorage = Versions[i].Up(keyStorage);

            var hdKeyStorage = (HdKeyStorage) keyStorage;

            if (storageVersion.Version != new Version(HdKeyStorage.CurrentVersion))
            {
                hdKeyStorage.Encrypt(password);
                hdKeyStorage.SaveToFile(pathToFile, password);
            }

            return hdKeyStorage;
        }
    }
}