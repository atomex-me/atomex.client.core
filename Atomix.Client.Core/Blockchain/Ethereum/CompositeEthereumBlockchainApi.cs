using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Atomix.Blockchain.Abstract;
using Nethereum.Contracts;
using Nethereum.Signer;

namespace Atomix.Blockchain.Ethereum
{
    public class CompositeEthereumBlockchainApi : IEthereumBlockchainApi
    {
        private readonly Web3BlockchainApi _web3;
        private readonly EtherScanApi _etherScanApi;

        public CompositeEthereumBlockchainApi(Chain chain)
        {
            _web3 = new Web3BlockchainApi(chain);
            _etherScanApi = new EtherScanApi(chain);
        }

        public Task<IBlockchainTransaction> GetTransactionAsync(
            string txId,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            return _web3.GetTransactionAsync(txId, cancellationToken);
        }

        public Task<string> BroadcastAsync(
            IBlockchainTransaction transaction,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            return _web3.BroadcastAsync(transaction, cancellationToken);
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
    }
}