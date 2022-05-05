using System;
using System.Collections.Generic;
using System.Threading;

namespace Atomex.Wallets.Ethereum
{
    public class EthereumOfflineNonce
    {
        private readonly Dictionary<string, long> _offlineNonces;

        private static EthereumOfflineNonce _instance;
        public static EthereumOfflineNonce Instance
        {
            get {
                var instance = _instance;

                if (instance == null)
                {
                    Interlocked.CompareExchange(ref _instance, new EthereumOfflineNonce(), null);
                    instance = _instance;
                }

                return instance;
            }
        }

        public EthereumOfflineNonce()
        {
            _offlineNonces = new Dictionary<string, long>();
        }

        public long GetOfflineNonce(
            string address,
            long nonceFromNetwork,
            bool isPendingNonce)
        {
            lock (_offlineNonces)
            {
                var currentNonce = _offlineNonces.TryGetValue(address, out var offlineNonce) && offlineNonce > nonceFromNetwork && !isPendingNonce
                    ? offlineNonce
                    : nonceFromNetwork;

                _offlineNonces[address] = currentNonce + 1;

                return currentNonce;
            }
        }

        public void RollbackOfflineNonce(
            string address,
            long difference)
        {
            lock (_offlineNonces)
            {
                if (_offlineNonces.TryGetValue(address, out var offlineNonce))
                {
                    _offlineNonces[address] = Math.Max(offlineNonce + difference, 0);
                }
            }
        }
    }
}