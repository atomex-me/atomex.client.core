using System;
using System.Collections.Generic;
using System.Threading;

namespace Atomex.Wallets.Tezos
{
    public class TezosOfflineCounter
    {
        private readonly TimeSpan ExpirationPeriod = TimeSpan.FromMinutes(10); // ~ 10 blocks

        private static TezosOfflineCounter _instance;
        public static TezosOfflineCounter Instance
        {
            get {
                var instance = _instance;

                if (instance == null)
                {
                    Interlocked.CompareExchange(ref _instance, new TezosOfflineCounter(), null);
                    instance = _instance;
                }

                return instance;
            }
        }

        private class CounterEntry
        {
            public int Value { get; set; }
            public DateTimeOffset Expiration { get; set; }
        }

        private readonly Dictionary<string, CounterEntry> _offlineCounters;

        private TezosOfflineCounter()
        {
            _offlineCounters = new Dictionary<string, CounterEntry>();
        }

        public int GetOfflineCounter(
            string address,
            int counterFromNetwork,
            int numberOfCounters = 1)
        {
            lock (_offlineCounters)
            {
                if (_offlineCounters.TryGetValue(address, out var offlineCounterEntry))
                {
                    // update offline counter
                    var offlineCounter = offlineCounterEntry.Value < counterFromNetwork || DateTimeOffset.UtcNow > offlineCounterEntry.Expiration
                        ? counterFromNetwork
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
                        Value = counterFromNetwork + numberOfCounters,
                        Expiration = DateTimeOffset.UtcNow + ExpirationPeriod
                    };

                    return ++counterFromNetwork;
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