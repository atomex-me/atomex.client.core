using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Atomix.Blockchain.Abstract;
using Atomix.Common;
using Atomix.Core.Entities;
using Nethereum.Signer;

namespace Atomix.Blockchain.Ethereum
{
    public class CompositeEthereumBlockchainApi : IEthereumBlockchainApi
    {
        private readonly Web3BlockchainApi _web3;
        private readonly EtherScanApi _etherScanApi;

        public CompositeEthereumBlockchainApi(Currency currency, Chain chain)
        {
            _web3 = new Web3BlockchainApi(currency, chain);
            _etherScanApi = new EtherScanApi(currency, chain);
        }

        public Task<decimal> GetBalanceAsync(
            string address,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            return _web3.GetBalanceAsync(address, cancellationToken);
        }

        public Task<BigInteger> GetTransactionCountAsync(
            string address,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            return _web3.GetTransactionCountAsync(address, cancellationToken);
        }

        public Task<IEnumerable<IBlockchainTransaction>> GetTransactionsAsync(
            string address,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            return _etherScanApi.GetTransactionsAsync(address, cancellationToken);
        }

        public Task<IBlockchainTransaction> GetTransactionAsync(
            string txId,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            return _web3.GetTransactionAsync(txId, cancellationToken); //_etherScanApi.GetTransactionAsync(txId, cancellationToken); 
        }

        public async Task<IEnumerable<IBlockchainTransaction>> GetTransactionsByIdAsync(
            string txId,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            var tx = await _web3.GetTransactionAsync(txId, cancellationToken)
                .ConfigureAwait(false);

            if (tx == null)
                return Enumerable.Empty<IBlockchainTransaction>();

            var internalTransactions = await _etherScanApi
                .GetInternalTransactionsAsync(txId, cancellationToken)
                .ConfigureAwait(false);

            return new List<IBlockchainTransaction>() {tx}.AddRangeEx(internalTransactions);
        }

        public async Task<string> BroadcastAsync(
            IBlockchainTransaction transaction,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            return await _web3.BroadcastAsync(transaction, cancellationToken)
                .ConfigureAwait(false);
        }
    }
}