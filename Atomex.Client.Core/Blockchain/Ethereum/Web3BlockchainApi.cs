using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Atomex.Blockchain.Abstract;
using Atomex.Common;
using Atomex.Core;
using Nethereum.Signer;
using Nethereum.Web3;

namespace Atomex.Blockchain.Ethereum
{
    public class Web3BlockchainApi : BlockchainApi, IEthereumBlockchainApi
    {
        private const string InfuraMainNet = "https://mainnet.infura.io/v3/df01d4ef450640a2a48d9af4c2078eaf";
        private const string InfuraRinkeby = "https://rinkeby.infura.io/v3/df01d4ef450640a2a48d9af4c2078eaf";
        private const string InfuraRopsten = "https://ropsten.infura.io/v3/df01d4ef450640a2a48d9af4c2078eaf";

        private readonly Currency _currency;
        private readonly string _uri;

        public Web3BlockchainApi(Currency currency, Chain chain)
        {
            _currency = currency;
            _uri = UriByChain(chain);

            if (_uri == null)
                throw new NotSupportedException($"Chain {chain} not supported");
        }

        public override async Task<Result<decimal>> GetBalanceAsync(
            string address,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var web3 = new Web3(_uri);

                var balance = await web3.Eth.GetBalance
                    .SendRequestAsync(address)
                    .ConfigureAwait(false);

                return balance != null
                    ? Atomex.Ethereum.WeiToEth(balance.Value)
                    : 0;
            }
            catch (Exception e)
            {
                return new Error(Errors.RequestError, e.Message);
            }
        }

        public async Task<Result<BigInteger>> GetTransactionCountAsync(
            string address,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var web3 = new Web3(_uri);

                var result = await web3.Eth.Transactions
                    .GetTransactionCount
                    .SendRequestAsync(address)
                    .ConfigureAwait(false);

                return new Result<BigInteger>(result);
            }
            catch (Exception e)
            {
                return new Error(Errors.RequestError, e.Message);
            }
        }

        public async Task<Result<BigInteger>> TryGetTransactionCountAsync(
            string address,
            int attempts = 3,
            int attemptsIntervalMs = 1000,
            CancellationToken cancellationToken = default)
        {
            return await ResultHelper.TryDo((c) => GetTransactionCountAsync(address, c), attempts, attemptsIntervalMs, cancellationToken)
                .ConfigureAwait(false) ?? new Error(Errors.RequestError, $"Connection error while getting transaction count after {attempts} attempts");
        }

        public override async Task<Result<IBlockchainTransaction>> GetTransactionAsync(
            string txId,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var web3 = new Web3(_uri);

                var tx = await web3.Eth.Transactions
                    .GetTransactionByHash
                    .SendRequestAsync(txId)
                    .ConfigureAwait(false);

                if (tx?.BlockHash == null)
                    return new Result<IBlockchainTransaction>((IBlockchainTransaction)null);

                var block = await web3.Eth.Blocks
                    .GetBlockWithTransactionsHashesByHash
                    .SendRequestAsync(tx.BlockHash)
                    .ConfigureAwait(false);

                var utcTimeStamp = block != null
                    ? ((long)block.Timestamp.Value).ToUtcDateTime()
                    : DateTime.UtcNow;

                var txReceipt = await web3.Eth.Transactions
                    .GetTransactionReceipt
                    .SendRequestAsync(txId)
                    .ConfigureAwait(false);

                if (txReceipt == null)
                {
                    //Log.Error("Tx not null, but txReceipt is null for txIs {@txId}!", txId);
                    return new Result<IBlockchainTransaction>((IBlockchainTransaction)null);
                }

                return new EthereumTransaction(_currency, tx, txReceipt, utcTimeStamp);
            }
            catch (Exception e)
            {
                return new Error(Errors.RequestError, e.Message);
            }
        }

        public Task<Result<IEnumerable<IBlockchainTransaction>>> GetTransactionsAsync(
            string address,
            CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public Task<Result<IEnumerable<IBlockchainTransaction>>> TryGetTransactionsAsync(
            string address,
            int attempts = 3,
            int attemptsIntervalMs = 1000,
            CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public override async Task<Result<string>> BroadcastAsync(
            IBlockchainTransaction transaction,
            CancellationToken cancellationToken = default)
        {
            try
            {
                if (!(transaction is EthereumTransaction ethTx))
                    throw new NotSupportedException("Not supported transaction type");

                transaction.State = BlockchainTransactionState.Pending;

                var web3 = new Web3(_uri);

                var txId = await web3.Eth.Transactions
                    .SendRawTransaction
                    .SendRequestAsync("0x" + ethTx.RlpEncodedTx)
                    .ConfigureAwait(false);

                ethTx.Id = txId; // todo: wtf?

                return txId;
            }
            catch (Nethereum.JsonRpc.Client.RpcResponseException e)
            {
                return new Error(Errors.RpcResponseError, e.RpcError?.Message);
            }
            catch (Exception e)
            {
                return new Error(Errors.RequestError, e.Message);
            }
        }

        public static string UriByChain(Chain chain)
        {
            return chain switch
            {
                Chain.MainNet => InfuraMainNet,
                Chain.Ropsten => InfuraRopsten,
                Chain.Rinkeby => InfuraRinkeby,
                _ => null,
            };
        }
    }
}