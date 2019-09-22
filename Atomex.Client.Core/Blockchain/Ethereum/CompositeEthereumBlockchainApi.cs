using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Atomex.Blockchain.Abstract;
using Atomex.Common;
using Atomex.Core.Entities;
using Nethereum.Signer;

namespace Atomex.Blockchain.Ethereum
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
            return _etherScanApi
                .GetTransactionsAsync(address, cancellationToken);
        }

        public async Task<IBlockchainTransaction> GetTransactionAsync(
            string txId,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            var tx = (EthereumTransaction)await _web3
                .GetTransactionAsync(txId, cancellationToken)
                .ConfigureAwait(false); //_etherScanApi.GetTransactionAsync(txId, cancellationToken);

            if (tx == null)
                return null;

            var internalTxs = (await _etherScanApi
                .GetInternalTransactionsAsync(txId, cancellationToken)
                .ConfigureAwait(false))
                .ToList();

            if (internalTxs.Any())
            {
                tx.InternalTxs = internalTxs
                    .Cast<EthereumTransaction>()
                    .ToList()
                    .ForEachDo(itx => itx.State = tx.State)
                    .ToList();
            }

            return tx;
        }

        public async Task<string> BroadcastAsync(
            IBlockchainTransaction transaction,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            return await _web3
                .BroadcastAsync(transaction, cancellationToken)
                .ConfigureAwait(false);
        }
    }
}