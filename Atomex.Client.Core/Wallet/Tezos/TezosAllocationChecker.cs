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
            if (_cache.TryGetValue(address, out var cacheValue))
            {
                if (cacheValue.TimeStamp + UpdateInterval > DateTimeOffset.UtcNow)
                    return cacheValue.Allocated;
            }

            var isAllocatedResult = await new TzktApi(_tezosConfig)
                .IsAllocatedAsync(address, cancellationToken)
                .ConfigureAwait(false);

            if (isAllocatedResult == null)
            {
                Log.Error("Error while checking allocation status for address {@address}", address);

                return false;
            }

            if (isAllocatedResult.HasError && isAllocatedResult.Error.Code != (int)HttpStatusCode.NotFound)
            {
                Log.Error("Error while checking allocation status for address {@address}. Code: {@code}. Description: {@desc}",
                    address,
                    isAllocatedResult.Error.Code,
                    isAllocatedResult.Error.Description);

                return false;
            }

            _cache[address] = new TezosAddressAllocationCache(isAllocatedResult.Value, DateTimeOffset.UtcNow);

            return isAllocatedResult.Value;
        }
    }
}