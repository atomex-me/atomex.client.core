using System;
using System.Collections.Generic;
using System.IO;
using System.Security;

namespace Atomex.Wallet.KeyStorage
{
    public static class HdKeyStorageLoader
    {
        private class StorageVersion
        {
            public Version Version { get; set; }
            public Func<string, SecureString, object> LoadFromFile { get; set; }
            public Func<object, SecureString, object> Up { get; set; }
        }

        private static IList<StorageVersion> PreviousVersions { get; } = new List<StorageVersion>
        {   
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

            foreach (var version in PreviousVersions)
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

            var versionIndex = PreviousVersions.IndexOf(storageVersion);

            // apply migrations
            for (var i = versionIndex; i >= 0; --i)
                keyStorage = PreviousVersions[i].Up(keyStorage, password);

            var hdKeyStorage = (HdKeyStorage)keyStorage;

            return hdKeyStorage;
        }
    }
}