using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Atomex.Blockchain.Abstract;
using Atomex.Common;
using Atomex.Core;
using Atomex.Core.Entities;
using Nethereum.Signer;
using Nethereum.Web3;

namespace Atomex.Blockchain.Ethereum
{
    public class Web3BlockchainApi : IEthereumBlockchainApi
    {
        private const string InfuraMainNet = "https://mainnet.infura.io";
        private const string InfuraRinkeby = "https://rinkeby.infura.io";
        private const string InfuraRopsten = "https://ropsten.infura.io";

        private readonly Currency _currency;
        private readonly string _uri;

        public Web3BlockchainApi(Currency currency, Chain chain)
        {
            _currency = currency;
            _uri = UriByChain(chain);

            if (_uri == null)
                throw new NotSupportedException($"Chain {chain} not supported");
        }

        public async Task<Result<decimal>> GetBalanceAsync(
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

        public async Task<Result<IBlockchainTransaction>> GetTransactionAsync(
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

        public async Task<Result<string>> BroadcastAsync(
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
            catch (Exception e)
            {
                return new Error(Errors.RequestError, e.Message);
            }
        }

        public static string UriByChain(Chain chain)
        {
            switch (chain)
            {
                case Chain.MainNet:
                    return InfuraMainNet;
                case Chain.Ropsten:
                    return InfuraRopsten;
                case Chain.Rinkeby:
                    return InfuraRinkeby; 
                default:
                    return null;
            }
        }
    }
}