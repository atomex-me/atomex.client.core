using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

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
                if (_offlineCounters.TryGetValue(address, out var offlineCounter))
                {
                    if (offlineCounter < currentCounter)
                        _offlineCounters[address] = currentCounter;
                }
                else
                {
                    _offlineCounters[address] = currentCounter;
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
                if (_offlineCounters.TryGetValue(address, out var offlineCounter))
                {
                    // update offline counter
                    if (offlineCounter < currentCounter)
                        offlineCounter = currentCounter;

                    _offlineCounters[address] = offlineCounter + numberOfCounters;

                    return ++offlineCounter;
                }
                else
                {
                    _offlineCounters.Add(address, currentCounter + numberOfCounters);

                    return ++currentCounter;
                }
            }
        }
    }
}