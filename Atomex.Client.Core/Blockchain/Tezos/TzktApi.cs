using System;
using System.Threading;
using System.Threading.Tasks;
using Atomex.Common;
using Atomex.Core;
using Atomex.Wallet.Tezos;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Atomex.Blockchain.Tezos
{
    public class TzktApi
    {
        private const string MainNetUri = "https://api.tzkt.io/v1/";
        private const string TestNetUri = "https://api.babylon.tzkt.io/v1/";

        public string BaseUri { get; set; }

        public TzktApi(Network network)
        {
            BaseUri = network switch
            {
                Network.MainNet => MainNetUri,
                Network.TestNet => TestNetUri,
                _ => throw new NotSupportedException($"Network {network} not supported")
            };
        }

        public async Task<Result<TezosAddressInfo>> GetAddressInfoAsync(
            string address,
            CancellationToken cancellationToken = default)
        {
            return await HttpHelper.GetAsyncResult<TezosAddressInfo>(
                    baseUri: BaseUri,
                    requestUri: $"Accounts/{address}",
                    responseHandler: (response, content) =>
                    {
                        var addressInfo = JsonConvert.DeserializeObject<JObject>(content);

                        var type = addressInfo["type"].Value<string>();

                        if (type == "empty")
                        {
                            return new TezosAddressInfo
                            {
                                Address = address,
                                IsAllocated = false,
                                IsRevealed = false,
                                LastCheckTimeUtc = DateTime.UtcNow
                            };
                        }

                        if (type == "user")
                        {
                            return new TezosAddressInfo
                            {
                                Address = address,
                                IsAllocated = decimal.Parse(addressInfo["balance"].Value<string>()) > 0,
                                IsRevealed = addressInfo["revealed"].Value<bool>(),
                                LastCheckTimeUtc = DateTime.UtcNow
                            };
                        }

                        return new TezosAddressInfo
                        {
                            Address = address,
                            IsAllocated = true,
                            IsRevealed = true,
                            LastCheckTimeUtc = DateTime.UtcNow
                        };
                    },
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }

        public async Task<Result<bool>> IsAllocatedAsync(
            string address,
            CancellationToken cancellationToken = default)
        {
            var addressInfo = await GetAddressInfoAsync(address, cancellationToken)
                .ConfigureAwait(false);

            if (addressInfo.HasError)
                return addressInfo.Error;

            return addressInfo.Value.IsAllocated;
        }

        public async Task<Result<bool>> IsRevealedAsync(
            string address,
            CancellationToken cancellationToken = default)
        {
            var addressInfo = await GetAddressInfoAsync(address, cancellationToken)
                .ConfigureAwait(false);

            if (addressInfo.HasError)
                return addressInfo.Error;

            return addressInfo.Value.IsRevealed;
        }
    }
}
