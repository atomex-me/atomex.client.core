using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json;

using Atomix.Updates.Abstract;

namespace Atomix.Updates
{
    public class HttpMetadataProvider : IBinariesProvider, IVersionProvider
    {
        readonly string MetadataUri;
        readonly string Platform;

        public HttpMetadataProvider(string uri, TargetPlatform platform)
        {
            if (String.IsNullOrEmpty(uri))
                throw new ArgumentNullException();

            if (!Uri.IsWellFormedUriString(uri, UriKind.Absolute))
                throw new ArgumentException("Invalid uri");

            MetadataUri = uri;
            Platform = platform == TargetPlatform.Windows ? "windows"
                : throw new ArgumentException("Platform is not supported");
        }

        public async Task<Stream> GetLatestBinariesAsync()
        {
            using (var http = new HttpClient() { Timeout = TimeSpan.FromSeconds(10) })
            {
                return await http.GetStreamAsync((await GetMetadataAsync()).Url);
            }
        }

        public async Task<Version> GetLatestVersionAsync()
        {
            return (await GetMetadataAsync()).Version;
        }

        async Task<PackageMetadata> GetMetadataAsync()
        {
            using (var http = new HttpClient() { Timeout = TimeSpan.FromSeconds(10) })
            {
                var content = await http.GetStringAsync(MetadataUri);
                var metadata = JsonConvert.DeserializeObject<Dictionary<string, PackageMetadata>>(content);

                return metadata.ContainsKey(Platform) ? metadata[Platform]
                    : throw new Exception($"Metadata for {Platform} platform is missed");
            }
        }

        class PackageMetadata
        {
            [JsonProperty("version", Required = Required.Always)]
            public Version Version { get; set; }

            [JsonProperty("url", Required = Required.Always)]
            public string Url { get; set; }
        }
    }

    public enum TargetPlatform
    {
        Windows
    }

    public static class HttpMetadataProviderExt
    {
        public static Updater UseHttpMetadataProvider(this Updater updater, string uri, TargetPlatform platform)
        {
            var provider = new HttpMetadataProvider(uri, platform);
            return updater.UseBinariesProvider(provider).UseVersionProvider(provider);
        }
    }
}
