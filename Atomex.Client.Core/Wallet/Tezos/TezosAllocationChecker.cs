using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Atomex.Blockchain.Tezos;
using Serilog;

namespace Atomex.Wallet.Tezos
{
    public class TezosAllocationChecker
    {
        private readonly Atomex.Tezos _tezos;
        private readonly IDictionary<string, TezosAddressInfo> _addresses;

        public TimeSpan UpdateInterval { get; set; } = TimeSpan.FromSeconds(60);

        public TezosAllocationChecker(Atomex.Tezos tezos)
        {
            _tezos = tezos;
            _addresses = new Dictionary<string, TezosAddressInfo>();
        }

        public async Task<bool> IsAllocatedAsync(
            string address,
            CancellationToken cancellationToken)
        {
            lock (_addresses)
            {
                if (_addresses.TryGetValue(address, out var info))
                {
                    if (info.LastCheckTimeUtc + UpdateInterval > DateTime.UtcNow)
                        return info.IsAllocated;
                }
            }

            var isAllocatedResult = await new TzktApi(_tezos)
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

            lock (_addresses)
            {
                if (_addresses.TryGetValue(address, out var info))
                {
                    info.Address = address;
                    info.IsAllocated = isAllocatedResult.Value;
                    info.LastCheckTimeUtc = DateTime.UtcNow;
                }
                else
                {
                    _addresses.Add(address, new TezosAddressInfo()
                    {
                        Address = address,
                        IsAllocated = isAllocatedResult.Value,
                        LastCheckTimeUtc = DateTime.UtcNow
                    });
                }
            }

            return isAllocatedResult.Value;
        }
    }
}