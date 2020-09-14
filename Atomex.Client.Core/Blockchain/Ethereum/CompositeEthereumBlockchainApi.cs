using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Atomex.Blockchain.Abstract;
using Atomex.Common;
using Atomex.Core;
using Nethereum.Signer;
using Atomex.Blockchain.Ethereum.ERC20;
using Nethereum.Contracts;
using Atomex.Blockchain.Ethereum.Abstract;

namespace Atomex.Blockchain.Ethereum
{
    public class CompositeEthereumBlockchainApi : BlockchainApi, IEthereumBlockchainApi, IGasPriceProvider
    {
        private readonly Web3BlockchainApi _web3;
        private readonly EtherScanApi _etherScanApi;

        public CompositeEthereumBlockchainApi(Atomex.Ethereum currency, Chain chain)
        {
            _web3 = new Web3BlockchainApi(currency, chain);
            _etherScanApi = new EtherScanApi(currency);
        }

        public override Task<Result<decimal>> GetBalanceAsync(
            string address,
            CancellationToken cancellationToken = default)
        {
            return _web3.GetBalanceAsync(address, cancellationToken);
        }

        public async Task<Result<decimal>> GetERC20AllowanceAsync(
            EthereumTokens.ERC20 erc20,
            string tokenAddress,
            FunctionMessage allowanceMessage,
            CancellationToken cancellationToken = default)
        {
            return await _web3.GetERC20AllowanceAsync(
                    erc20: erc20,
                    tokenAddress: tokenAddress,
                    allowanceMessage: allowanceMessage,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false); 
        }
        
        public Task<Result<BigInteger>> GetTransactionCountAsync(
            string address,
            CancellationToken cancellationToken = default)
        {
            return _web3.GetTransactionCountAsync(address, cancellationToken);
        }

        public async Task<Result<BigInteger>> TryGetTransactionCountAsync(
            string address,
            int attempts = 10,
            int attemptsIntervalMs = 1000,
            CancellationToken cancellationToken = default)
        {
            return await ResultHelper.TryDo((c) => GetTransactionCountAsync(address, c), attempts, attemptsIntervalMs, cancellationToken)
                .ConfigureAwait(false) ?? new Error(Errors.RequestError, $"Connection error while getting transaction count after {attempts} attempts");
        }

        public Task<Result<IEnumerable<IBlockchainTransaction>>> GetTransactionsAsync(
            string address,
            CancellationToken cancellationToken = default)
        {
            return _etherScanApi
                .GetTransactionsAsync(address, cancellationToken);
        }

        public async Task<Result<IEnumerable<IBlockchainTransaction>>> TryGetTransactionsAsync(
            string address,
            int attempts = 10,
            int attemptsIntervalMs = 1000,
            CancellationToken cancellationToken = default)
        {
            return await ResultHelper.TryDo((c) => GetTransactionsAsync(address, c), attempts, attemptsIntervalMs, cancellationToken)
                .ConfigureAwait(false) ?? new Error(Errors.RequestError, $"Connection error while getting transactions after {attempts} attempts");
        }

        public override async Task<Result<IBlockchainTransaction>> GetTransactionAsync(
            string txId,
            CancellationToken cancellationToken = default)
        {
            var txResult = await _web3
                .GetTransactionAsync(txId, cancellationToken)
                .ConfigureAwait(false); //_etherScanApi.GetTransactionAsync(txId, cancellationToken);

            if (txResult == null)
                return new Error(Errors.RequestError, "Connection error while getting transaction");

            if (txResult.HasError || txResult.Value == null)
                return txResult;

            var tx = (EthereumTransaction)txResult.Value;

            if (tx.Currency.Name != "ETH")
                tx = tx.ParseERC20Input();
            
            var internalTxsResult = await _etherScanApi
                .GetInternalTransactionsAsync(txId, cancellationToken)
                .ConfigureAwait(false);

            if (internalTxsResult == null)
                return new Error(Errors.RequestError, "Connection error while getting internal transactions");

            if (internalTxsResult.HasError)
                return internalTxsResult.Error;

            if (internalTxsResult.Value.Any())
            {
                tx.InternalTxs = internalTxsResult.Value
                    .Cast<EthereumTransaction>()
                    .ToList()
                    .ForEachDo(itx => itx.State = tx.State)
                    .ToList();
            }

            return txResult;
        }

        public override async Task<Result<string>> BroadcastAsync(
            IBlockchainTransaction transaction,
            CancellationToken cancellationToken = default)
        {
            return await _web3
                .BroadcastAsync(transaction, cancellationToken)
                .ConfigureAwait(false);
        }

        public Task<Result<GasPrice>> GetGasPriceAsync(
            bool useCache = true,
            CancellationToken cancellationToken = default)
        {
            return _etherScanApi.GetGasPriceAsync(useCache, cancellationToken);
        }
    }
}