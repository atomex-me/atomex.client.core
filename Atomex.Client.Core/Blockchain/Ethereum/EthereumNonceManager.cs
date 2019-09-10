using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;

namespace Atomex.Blockchain.Ethereum
{
    public class EthereumNonceManager
    {
        private TimeSpan ExpirationTimeOut { get; } = TimeSpan.FromSeconds(180);

        private class NonceEntry
        {
            public BigInteger Value { get; set; }
            public DateTime LastUpdatedTimeUtc { get; set; }
        }

        private IDictionary<string, NonceEntry> _nonces;
        private readonly object _syncRoot;

        private EthereumNonceManager()
        {
            _nonces = new Dictionary<string, NonceEntry>();
            _syncRoot = new object();
        }

        private static EthereumNonceManager _instance;
        public static EthereumNonceManager Instance
        {
            get {
                if (_instance == null)
                    _instance = new EthereumNonceManager();

                return _instance;
            }
        }

        public async Task<BigInteger> GetNonce(Atomex.Ethereum ethereum, string address)
        {
            var nonce = await ((IEthereumBlockchainApi)ethereum.BlockchainApi)
                .GetTransactionCountAsync(address)
                .ConfigureAwait(false);

            lock (_syncRoot)
            {
                if (_nonces.TryGetValue(address, out var offlineNonce))
                {
                    if (offlineNonce.Value >= nonce &&
                        DateTime.UtcNow - offlineNonce.LastUpdatedTimeUtc <= ExpirationTimeOut)
                    {
                        return offlineNonce.Value++;
                    }
                    else
                    {
                        _nonces[address] = new NonceEntry
                        {
                            Value = nonce + 1,
                            LastUpdatedTimeUtc = DateTime.UtcNow
                        };

                        return nonce;
                    }
                }
                else
                {
                    _nonces.Add(address, new NonceEntry
                    {
                        Value = nonce + 1,
                        LastUpdatedTimeUtc = DateTime.UtcNow
                    });

                    return nonce;
                }
            }
        }
    }
}