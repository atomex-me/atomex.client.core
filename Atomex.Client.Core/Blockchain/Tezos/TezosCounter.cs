using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Atomex.Blockchain.Tezos.Internal;
using Newtonsoft.Json.Linq;

namespace Atomex.Blockchain.Tezos
{
    public class TezosCounter
    {
        private TimeSpan ExpirationTimeOut { get; } = TimeSpan.FromSeconds(90);

        private class CounterEntry
        {
            public int Value { get; set; }
            public DateTime LastUpdatedTimeUtc { get; set; } 
        }

        private IDictionary<string, CounterEntry> _counters;
        private readonly object _syncRoot;

        private TezosCounter()
        {
            _counters = new Dictionary<string, CounterEntry>();
            _syncRoot = new object();
        }

        private static TezosCounter _instance;
        public static TezosCounter Instance
        {
            get
            {
                if (_instance == null)
                    _instance = new TezosCounter();

                return _instance;
            }
        }

        public async Task<int> GetCounter(Atomex.Tezos tezos, string address, JObject head)
        {
            var rpc = new Rpc(tezos.RpcProvider);

            var account = await rpc
                .GetAccountForBlock(head["hash"].ToString(), address)
                .ConfigureAwait(false);

            var counter = int.Parse(account["counter"].ToString());

            lock (_syncRoot)
            {
                if (_counters.TryGetValue(address, out var offlineCounter))
                {
                    if (offlineCounter.Value > counter &&
                        DateTime.UtcNow - offlineCounter.LastUpdatedTimeUtc <= ExpirationTimeOut)
                    {
                        return ++offlineCounter.Value;
                    }
                    else
                    {
                        ++counter;
                        _counters[address] = new CounterEntry
                        {
                            Value = counter,
                            LastUpdatedTimeUtc = DateTime.UtcNow
                        };

                        return counter;
                    }
                }
                else
                {
                    ++counter;
                    _counters.Add(address, new CounterEntry
                    {
                        Value = counter,
                        LastUpdatedTimeUtc = DateTime.UtcNow
                    });

                    return counter;
                }
            }
        }

        public async Task<int> GetCounter(Atomex.Tezos tezos, string address)
        {
            var rpc = new Rpc(tezos.RpcProvider);

            var head = await rpc
                .GetHeader()
                .ConfigureAwait(false);

            return await GetCounter(tezos, address, head)
                .ConfigureAwait(false);
        }
    }
}