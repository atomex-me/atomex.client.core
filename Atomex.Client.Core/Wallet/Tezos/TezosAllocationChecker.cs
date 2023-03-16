using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

using Serilog;

using Atomex.Blockchain.Tezos.Tzkt;

namespace Atomex.Wallet.Tezos
{
    public class TezosAllocationChecker
    {
        private record struct TezosAddressAllocationCache(bool Allocated, DateTimeOffset TimeStamp);

        private readonly TezosConfig _tezosConfig;
        private readonly IDictionary<string, TezosAddressAllocationCache> _cache;

        public TimeSpan UpdateInterval { get; set; } = TimeSpan.FromSeconds(60);

        public TezosAllocationChecker(TezosConfig tezosConfig)
        {
            _tezosConfig = tezosConfig;
            _cache = new Dictionary<string, TezosAddressAllocationCache>();
        }

        public async Task<bool> IsAllocatedAsync(
            string address,
            CancellationToken cancellationToken)
        {
            if (_cache.TryGetValue(address, out var cacheValue) && cacheValue.TimeStamp + UpdateInterval > DateTimeOffset.UtcNow)
                return cacheValue.Allocated;

            var (isAllocated, error) = await new TzktApi(_tezosConfig.GetTzktSettings())
                .IsAllocatedAsync(address, cancellationToken)
                .ConfigureAwait(false);

            if (error != null && error.Value.Code != (int)HttpStatusCode.NotFound)
            {
                Log.Error("Error while checking allocation status for address {@address}. Code: {@code}. Message: {@message}",
                    address,
                    error.Value.Code,
                    error.Value.Message);

                return false;
            }

            _cache[address] = new TezosAddressAllocationCache(isAllocated, DateTimeOffset.UtcNow);

            return isAllocated;
        }
    }
}