using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;

using Atomex.Blockchain.Abstract;
using Atomex.Blockchain.Ethereum.Abstract;
using Atomex.Common;

namespace Atomex.Blockchain.Ethereum
{
    public class EthereumApiSettings
    {
    }

    public class EthereumApi : IEthereumApi
    {
        public EthereumApi(
            EthereumApiSettings settings,
            ILogger logger = null)
        {
        }

        public Task<(string txId, Error error)> BroadcastAsync(Transaction transaction, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public Task<(int? estimatedGas, Error error)> EstimateGasAsync(
            string to,
            string from = null,
            BigInteger? value = null,
            BigInteger? gasPrice = null,
            BigInteger? gasLimit = null,
            string data = null,
            CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public Task<(decimal balance, Error error)> GetBalanceAsync(
            string address,
            CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public Task<(decimal? gasPrice, Error error)> GetFastGasPriceAsync(
            CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public Task<(Transaction tx, Error error)> GetTransactionAsync(
            string txId,
            CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public Task<(IEnumerable<EthereumTransaction> txs, Error error)> GetTransactionsAsync(
            string address,
            DateTimeOffset fromTimeStamp,
            CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public Task<(IEnumerable<EthereumTransaction> txs, Error error)> GetTransactionsAsync(
            string address,
            ulong fromBlock = 0,
            ulong toBlock = ulong.MaxValue,
            CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public Task<(long? count, Error error)> GetTransactionsCountAsync(
            string address,
            bool pending = false,
            CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }
    }
}