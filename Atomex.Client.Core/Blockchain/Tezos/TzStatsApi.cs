using System;
using System.Threading;
using System.Threading.Tasks;
using Atomex.Common;
using Atomex.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Atomex.Blockchain.Tezos
{
    public class TzStatsApi
    {
        private const string MainNetUri = "https://api.tzstats.com/";
        private const string TestNetUri = "https://api.babylonnet.tzstats.com/";

        public string BaseUri { get; set; }

        public TzStatsApi(Network network)
        {
            BaseUri = network switch
            {
                Network.MainNet => MainNetUri,
                Network.TestNet => TestNetUri,
                _ => throw new NotSupportedException($"Network {network} not supported")
            };
        }

        public async Task<Result<bool>> IsRevealedAsync(
            string address,
            CancellationToken cancellationToken = default)
        {
            return await HttpHelper.GetAsyncResult<bool>(
                    baseUri: BaseUri,
                    requestUri: $"explorer/account/{address}",
                    responseHandler: (response, content) =>
                    {
                        var addressInfo = JsonConvert.DeserializeObject<JObject>(content);

                        return bool.Parse(addressInfo["is_revealed"].Value<string>());
                    },
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }
    }
}