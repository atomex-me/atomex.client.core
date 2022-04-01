using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

using Serilog;

using Atomex.Blockchain.Tezos.Tzkt;

namespace Atomex.Wallet.Tezos
{
    public partial class TezosRevealChecker
    {
        private readonly TezosConfig _tezos;
        private readonly IDictionary<string, TezosAddressInfo> _addresses;

        public TimeSpan UpdateInterval { get; set; } = TimeSpan.FromSeconds(30);

        public TezosRevealChecker(TezosConfig tezos)
        {
            _tezos = tezos;
            _addresses = new Dictionary<string, TezosAddressInfo>();
        }

        public async Task<bool> IsRevealedAsync(
            string address,
            CancellationToken cancellationToken)
        {
            lock (_addresses)
            {
                if (_addresses.TryGetValue(address, out var info))
                {
                    if (info.LastCheckTimeUtc + UpdateInterval > DateTime.UtcNow)
                        return info.IsRevealed;
                }
            }

            var isRevealedResult = await new TzktApi(_tezos)
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

            lock (_addresses)
            {
                if (_addresses.TryGetValue(address, out var info))
                {
                    info.Address = address;
                    info.IsRevealed = isRevealedResult.Value;
                    info.LastCheckTimeUtc = DateTime.UtcNow;
                }
                else
                {
                    _addresses.Add(address, new TezosAddressInfo()
                    {
                        Address = address,
                        IsRevealed = isRevealedResult.Value,
                        LastCheckTimeUtc = DateTime.UtcNow
                    });
                }
            }

            return isRevealedResult.Value;
        }
    }
}