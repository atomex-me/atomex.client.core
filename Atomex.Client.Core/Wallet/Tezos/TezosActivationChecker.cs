using Serilog;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Atomex.Blockchain.Tezos;
using Atomex.Core.Entities;

namespace Atomex.Wallet.Tezos
{
    public class TezosActivationChecker
    {
        private class TezosAddressInfo
        {
            public bool IsActivated { get; set; }
            public DateTime LastCheckTimeUtc { get; set; }
        }

        private readonly Currency _tezos;
        private readonly IDictionary<string, TezosAddressInfo> _addresses;

        public TimeSpan UpdateInterval { get; set; } = TimeSpan.FromSeconds(60);

        public TezosActivationChecker(Currency tezos)
        {
            _tezos = tezos;
            _addresses = new Dictionary<string, TezosAddressInfo>();
        }

        public async Task<bool> IsActivateAsync(
            string address,
            CancellationToken cancellationToken)
        {
            lock (_addresses)
            {
                if (_addresses.TryGetValue(address, out var info))
                {
                    if (info.LastCheckTimeUtc + UpdateInterval > DateTime.UtcNow)
                        return info.IsActivated;
                }
            }

            if (!(_tezos.BlockchainApi is ITezosBlockchainApi api))
            {
                Log.Error("Invalid api type");

                return false;
            }

            var isActiveResult = await api
                .IsActiveAddress(address, cancellationToken)
                .ConfigureAwait(false);

            if (isActiveResult.HasError)
            {
                Log.Error("Error while checking 'isActive' status for address {@address}. Code: {@code}. Description: {@desc}",
                    address,
                    isActiveResult.Error.Code,
                    isActiveResult.Error.Description);

                return false;
            }

            lock (_addresses)
            {
                if (_addresses.TryGetValue(address, out var info))
                {
                    info.IsActivated = isActiveResult.Value;
                    info.LastCheckTimeUtc = DateTime.UtcNow;
                }
                else
                {
                    _addresses.Add(address, new TezosAddressInfo()
                    {
                        IsActivated = isActiveResult.Value,
                        LastCheckTimeUtc = DateTime.UtcNow
                    });
                }
            }

            return isActiveResult.Value;
        }
    }
}