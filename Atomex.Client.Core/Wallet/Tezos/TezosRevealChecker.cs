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
            if (_cache.TryGetValue(address, out var cacheValue) && cacheValue.TimeStamp + UpdateInterval > DateTimeOffset.UtcNow)
                return cacheValue.Revealed;

            var (isRevealed, error) = await new TzktApi(_tezosConfig.GetTzktSettings())
                .IsRevealedAsync(address, cancellationToken)
                .ConfigureAwait(false);

            if (error != null && error.Value.Code != (int)HttpStatusCode.NotFound)
            {
                Log.Error("Error while checking reveal status for address {@address}. Code: {@code}. Message: {@message}",
                    address,
                    error.Value.Code,
                    error.Value.Message);

                return false;
            }

            _cache[address] = new TezosAddressRevealCache(isRevealed, DateTimeOffset.UtcNow);

            return isRevealed;
        }
    }
}