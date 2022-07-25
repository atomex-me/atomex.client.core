using System;
using System.Collections.Concurrent;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

using Serilog;

using Atomex.Blockchain.Tezos.Tzkt;

namespace Atomex.Wallet.Tezos
{
    public partial class TezosRevealChecker
    {
        private record struct TezosAddressRevealCache(bool Revealed, DateTimeOffset TimeStamp);

        private readonly TezosConfig _tezosConfig;
        private readonly ConcurrentDictionary<string, TezosAddressRevealCache> _cache;

        public TimeSpan UpdateInterval { get; set; } = TimeSpan.FromSeconds(30);

        public TezosRevealChecker(TezosConfig tezosConfig)
        {
            _tezosConfig = tezosConfig ?? throw new ArgumentNullException(nameof(tezosConfig));
            _cache = new ConcurrentDictionary<string, TezosAddressRevealCache>();
        }

        public async Task<bool> IsRevealedAsync(
            string address,
            CancellationToken cancellationToken)
        {
            if (_cache.TryGetValue(address, out var cacheValue))
            {
                if (cacheValue.TimeStamp + UpdateInterval > DateTimeOffset.UtcNow)
                    return cacheValue.Revealed;
            }

            var isRevealedResult = await new TzktApi(_tezosConfig)
                .IsRevealedAsync(address, cancellationToken)
                .ConfigureAwait(false);

            if (isRevealedResult == null)
            {
                Log.Error("Error while checking reveal status for address {@address}", address);

                return false;
            }

            if (isRevealedResult.HasError && isRevealedResult.Error.Code != (int)HttpStatusCode.NotFound)
            {
                Log.Error("Error while checking reveal status for address {@address}. Code: {@code}. Description: {@desc}",
                    address,
                    isRevealedResult.Error.Code,
                    isRevealedResult.Error.Description);

                return false;
            }

            _cache[address] = new TezosAddressRevealCache(isRevealedResult.Value, DateTimeOffset.UtcNow);

            return isRevealedResult.Value;
        }
    }
}