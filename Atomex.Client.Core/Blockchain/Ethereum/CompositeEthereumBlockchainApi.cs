using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Atomex.Blockchain.Abstract;
using Atomex.Common;
using Nethereum.Signer;

namespace Atomex.Blockchain.Ethereum
{
    public class CompositeEthereumBlockchainApi : IEthereumBlockchainApi
    {
        private readonly Web3BlockchainApi _web3;
        private readonly EtherScanApi _etherScanApi;

        public CompositeEthereumBlockchainApi(Atomex.Ethereum currency, Chain chain)
        {
            _web3 = new Web3BlockchainApi(currency, chain);
            _etherScanApi = new EtherScanApi(currency);
        }

        public Task<Result<decimal>> GetBalanceAsync(
            string address,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            return _web3.GetBalanceAsync(address, cancellationToken);
        }

        public Task<Result<BigInteger>> GetTransactionCountAsync(
            string address,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            return _web3.GetTransactionCountAsync(address, cancellationToken);
        }

        public Task<Result<IEnumerable<IBlockchainTransaction>>> GetTransactionsAsync(
            string address,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            return _etherScanApi
                .GetTransactionsAsync(address, cancellationToken);
        }

        public async Task<Result<IBlockchainTransaction>> GetTransactionAsync(
            string txId,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            var txAsyncResult = await _web3
                .GetTransactionAsync(txId, cancellationToken)
                .ConfigureAwait(false); //_etherScanApi.GetTransactionAsync(txId, cancellationToken);

            if (txAsyncResult.HasError || txAsyncResult.Value == null)
                return txAsyncResult;

            var tx = (EthereumTransaction)txAsyncResult.Value;

            var internalTxsAsyncResult = await _etherScanApi
                .GetInternalTransactionsAsync(txId, cancellationToken)
                .ConfigureAwait(false);

            if (internalTxsAsyncResult.HasError)
                return new Result<IBlockchainTransaction>(internalTxsAsyncResult.Error);

            if (internalTxsAsyncResult.Value.Any())
            {
                tx.InternalTxs = internalTxsAsyncResult.Value
                    .Cast<EthereumTransaction>()
                    .ToList()
                    .ForEachDo(itx => itx.State = tx.State)
                    .ToList();
            }

            return txAsyncResult;
        }

        public async Task<Result<string>> BroadcastAsync(
            IBlockchainTransaction transaction,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            return await _web3
                .BroadcastAsync(transaction, cancellationToken)
                .ConfigureAwait(false);
        }
    }
}