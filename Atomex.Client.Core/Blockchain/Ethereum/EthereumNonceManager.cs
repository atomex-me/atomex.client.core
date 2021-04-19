using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;

using Atomex.Common;

namespace Atomex.Blockchain.Ethereum
{
    public class EthereumNonceManager
    {
        private readonly IDictionary<string, BigInteger> _offlineNonces;

        private EthereumNonceManager()
        {
            _offlineNonces = new Dictionary<string, BigInteger>();
        }

        private static EthereumNonceManager _instance;
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
            Atomex.Ethereum ethereum,
            string address,
            bool pending = true,
            CancellationToken cancellationToken = default)
        {
            var transactionCountResult = await ((IEthereumBlockchainApi)ethereum.BlockchainApi)
                .GetTransactionCountAsync(address, pending, cancellationToken)
                .ConfigureAwait(false);

            if (transactionCountResult.HasError)
                return transactionCountResult;

            var nonceFromNetwork = transactionCountResult.Value;

            lock (_offlineNonces)
            {
                var currentNonce = _offlineNonces.TryGetValue(address, out var offlineNonce) && offlineNonce > nonceFromNetwork && !pending
                    ? offlineNonce
                    : nonceFromNetwork;

                _offlineNonces[address] = currentNonce + 1;

                return currentNonce;
            }
        }
    }
}