using System.Collections.Generic;
using System.Numerics;

namespace Atomex.Blockchain.Tezos
{
    public class ThumbsApiSettings
    {
        public string ThumbsApiUri { get; set; }
        public string IpfsGatewayUri { get; set; }
        public string CatavaApiUri { get; set; }

        // Contracts that use the same thumbnailUri for all tokens (will use displayUri instead)
        public List<string> Exceptions { get; set; } = new() { "KT1RJ6PbjHpwc3M5rw5s2Nbmefwbuwbdxton" };
    }

    public class ThumbsApi
    {
        private readonly ThumbsApiSettings _settings;

        public ThumbsApi(ThumbsApiSettings settings)
        {
            _settings = settings;
        }

        public static string RemovePrefix(string s, string prefix) =>
            s.StartsWith(prefix) ? s.Substring(prefix.Length) : s;

        public static string RemoveIpfsPrefix(string url) =>
            RemovePrefix(url, "ipfs://");

        public static bool HasIpfsPrefix(string url) =>
            url?.StartsWith("ipfs://") ?? false;

        public IEnumerable<string> GetTokenPreviewUrls(string contractAddress, string thumbnailUri, string displayUri)
        {
            var previewUrl = _settings.Exceptions.Contains(contractAddress)
                ? displayUri ?? thumbnailUri
                : thumbnailUri ?? displayUri;

            if (previewUrl != null)
            {
                if (HasIpfsPrefix(previewUrl))
                {
                    var cid = RemoveIpfsPrefix(previewUrl);
                    yield return $"{_settings.ThumbsApiUri}/{cid}";
                    yield return $"{_settings.IpfsGatewayUri}/{cid}";
                }
                else
                {
                    yield return previewUrl;
                }
            }

            yield return GetContractPreviewUrl(contractAddress);
        }

        public string GetContractPreviewUrl(string contractAddress)
        {
            return $"{_settings.CatavaApiUri}/{contractAddress}";
        }

        public static string GetTokenPreviewUrl(string contractAddress, BigInteger tokenId) =>
            $"https://services.atomex.me/tokens-preview/{contractAddress}/{tokenId}.png";
    }
}