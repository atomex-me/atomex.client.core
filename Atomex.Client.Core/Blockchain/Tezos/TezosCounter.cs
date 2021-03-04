using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Newtonsoft.Json.Linq;

using Atomex.Blockchain.Tezos.Internal;

namespace Atomex.Blockchain.Tezos
{
    public class TezosCounter
    {
        private readonly Dictionary<string, int> _offlineCounters;

        private TezosCounter()
        {
            _offlineCounters = new Dictionary<string, int>();
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

        public async Task<int> GetCounter(
            string address,
            JObject head,
            Atomex.Tezos tezosConfig,
            bool useOffline = false,
            int counters = 1)
        {
            var rpc = new Rpc(tezosConfig.RpcNodeUri);

            var account = await rpc
                .GetAccountForBlock(head["hash"].ToString(), address)
                .ConfigureAwait(false);

            var counter = int.Parse(account["counter"].ToString());

            if (!useOffline)
                return ++counter;

            lock (_offlineCounters)
            {
                if (_offlineCounters.TryGetValue(address, out var offlineCounter))
                {
                    _offlineCounters[address] = offlineCounter + counters;

                    return ++offlineCounter;
                }
                else
                {
                    _offlineCounters.Add(address, counter + counters);

                    return ++counter;
                }
            }
        }

        public void UpdateOfflineCounter(string address, int offlineCounter)
        {

        }
    }
}