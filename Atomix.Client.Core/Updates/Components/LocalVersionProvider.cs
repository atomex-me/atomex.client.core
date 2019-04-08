using System;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

using Atomix.Updates.Abstract;

namespace Atomix.Updates
{
    public class LocalVersionProvider : IVersionProvider
    {
        readonly string FilePath;

        public LocalVersionProvider(string filePath)
        {
            FilePath = filePath;
        }

        public Task<Version> GetLatestVersionAsync()
        {
            var json = JToken.Parse(File.ReadAllText(FilePath));
            var version = Version.Parse(json["version"].Value<string>());
            return Task.FromResult(version);
        }
    }

    public static class LocalVersionProviderExt
    {
        public static Updater UseLocalVersionProvider(this Updater updater, string filePath)
        {
            return updater.UseVersionProvider(new LocalVersionProvider(filePath));
        }
    }
}
