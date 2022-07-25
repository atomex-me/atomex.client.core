using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Atomex.Blockchain.Tezos.Internal;

namespace Atomex.Blockchain.Tezos
{
    public class TezosCounter
    {
        private readonly TimeSpan ExpirationPeriod = TimeSpan.FromMinutes(10); // ~ 10 blocks

        private class CounterEntry
        {
            public int Value { get; set; }
            public DateTimeOffset Expiration { get; set; }
        }

        private readonly Dictionary<string, CounterEntry> _offlineCounters;

        private TezosCounter()
        {
            _offlineCounters = new Dictionary<string, CounterEntry>();
        }

        private static TezosCounter _instance;
        public static TezosCounter Instance
        {
            get
            {
                var instance = _instance;

                if (instance == null)
                {
                    Interlocked.CompareExchange(ref _instance, new TezosCounter(), null);
                    instance = _instance;
                }

                return instance;
            }
        }

        private async Task<int> GetCounterFromRpcAsync(
            string address,
            string head,
            string rpcNodeUri)
        {
            var rpc = new Rpc(rpcNodeUri);

            var account = await rpc
                .GetAccountForBlock(head, address)
                .ConfigureAwait(false);

            return int.Parse(account["counter"].ToString());
        }

        public async Task<int> GetCounterAsync(
            string address,
            string head,
            string rpcNodeUri)
        {
            var currentCounter = await GetCounterFromRpcAsync(address, head, rpcNodeUri)
                .ConfigureAwait(false);

            // update offline counter if need
            lock (_offlineCounters)
            {
                if (_offlineCounters.TryGetValue(address, out var offlineCounterEntry))
                {
                    if (offlineCounterEntry.Value < currentCounter ||
                        DateTimeOffset.UtcNow > offlineCounterEntry.Expiration)
                    {
                        _offlineCounters[address] = new CounterEntry
                        {
                            Value = currentCounter,
                            Expiration = DateTimeOffset.UtcNow + ExpirationPeriod
                        };
                    }
                }
                else
                {
                    _offlineCounters[address] = new CounterEntry
                    {
                        Value = currentCounter,
                        Expiration = DateTimeOffset.UtcNow + ExpirationPeriod
                    };
                }
            }

            return ++currentCounter;
        }

        public async Task<int> GetOfflineCounterAsync(
            string address,
            string head,
            string rpcNodeUri,
            int numberOfCounters = 1)
        {
            var currentCounter = await GetCounterFromRpcAsync(address, head, rpcNodeUri)
                .ConfigureAwait(false);

            lock (_offlineCounters)
            {
                if (_offlineCounters.TryGetValue(address, out var offlineCounterEntry))
                {
                    // update offline counter
                    var offlineCounter = offlineCounterEntry.Value < currentCounter || DateTimeOffset.UtcNow > offlineCounterEntry.Expiration
                        ? currentCounter
                        : offlineCounterEntry.Value;

                    _offlineCounters[address] = new CounterEntry
                    {
                        Value = offlineCounter + numberOfCounters,
                        Expiration = DateTimeOffset.UtcNow + ExpirationPeriod
                    };

                    return ++offlineCounter;
                }
                else
                {
                    _offlineCounters[address] = new CounterEntry
                    {
                        Value = currentCounter + numberOfCounters,
                        Expiration = DateTimeOffset.UtcNow + ExpirationPeriod
                    };

                    return ++currentCounter;
                }
            }
        }

        public void RollbackOfflineCounter(
            string address,
            int numberOfCounters)
        {
            lock (_offlineCounters)
            {
                if (_offlineCounters.TryGetValue(address, out var offlineCounterEntry))
                {
                    offlineCounterEntry.Value -= numberOfCounters;
                    offlineCounterEntry.Expiration = DateTimeOffset.UtcNow + ExpirationPeriod;
                }
            }
        }
    }
}