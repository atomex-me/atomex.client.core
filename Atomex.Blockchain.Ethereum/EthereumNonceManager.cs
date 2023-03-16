using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;

using Atomex.Blockchain.Ethereum.Abstract;
using Atomex.Common;

namespace Atomex.Blockchain.Ethereum
{
    public class EthereumNonceManager
    {
        private class NonceEntry
        {
            public DateTimeOffset LastUpdated { get; set; }
            public BigInteger Nonce { get; set; }
        }

        private readonly IDictionary<string, NonceEntry> _offlineNonces;

        private EthereumNonceManager()
        {
            _offlineNonces = new Dictionary<string, NonceEntry>();
        }

        private static EthereumNonceManager? _instance;
        public static EthereumNonceManager Instance
        {
            get {
                var instance = _instance;

                if (instance == null)
                {
                    Interlocked.CompareExchange(ref _instance, new EthereumNonceManager(), null);
                    instance = _instance;
                }

                return instance;
            }
        }

        public async Task<Result<BigInteger>> GetNonceAsync(
            IEthereumApi api,
            string address,
            bool pending = true,
            CancellationToken cancellationToken = default)
        {
            var (transactionCount, error) = await api
                .GetTransactionsCountAsync(address, pending, cancellationToken)
                .ConfigureAwait(false);

            if (error != null)
                return error;

            var nonceFromNetwork = transactionCount;

            lock (_offlineNonces)
            {
                var currentNonce = _offlineNonces.TryGetValue(address, out var offlineNonce)
                    && offlineNonce.Nonce > nonceFromNetwork
                    && DateTimeOffset.UtcNow - offlineNonce.LastUpdated < TimeSpan.FromMinutes(5)
                    ? offlineNonce.Nonce
                    : nonceFromNetwork;

                _offlineNonces[address] = new NonceEntry
                {
                    Nonce = currentNonce + 1,
                    LastUpdated = DateTimeOffset.UtcNow
                };

                return currentNonce;
            }
        }
    }
}