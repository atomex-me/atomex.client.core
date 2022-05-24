using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;

using Atomex.Blockchain.Abstract;
using Atomex.Blockchain.Ethereum.Abstract;
using Atomex.Blockchain.Ethereum.Etherscan;
using Atomex.Common;

namespace Atomex.Blockchain.Ethereum
{
    public class EthereumApiSettings
    {
        public EtherScanSettings EtherScan { get; set; }
    }

    public class EthereumApi : IEthereumApi
    {
        private readonly EthereumApiSettings _settings;
        private readonly ILogger _logger;

        public EthereumApi(
            EthereumApiSettings settings,
            ILogger logger = null)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _logger = logger;
        }

        public Task<(string txId, Error error)> BroadcastAsync(
            Transaction transaction,
            CancellationToken cancellationToken = default)
        {
            return new EtherScanApi(_settings.EtherScan)
                .BroadcastAsync(transaction, cancellationToken);
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
            return new EtherScanApi(_settings.EtherScan)
                .EstimateGasAsync(to, from, value, gasPrice, gasLimit, data, cancellationToken);
        }

        public Task<(decimal balance, Error error)> GetBalanceAsync(
            string address,
            CancellationToken cancellationToken = default)
        {
            return new EtherScanApi(_settings.EtherScan)
                .GetBalanceAsync(address, cancellationToken);
        }

        public Task<(decimal? gasPrice, Error error)> GetFastGasPriceAsync(
            CancellationToken cancellationToken = default)
        {
            return new EtherScanApi(_settings.EtherScan)
                .GetFastGasPriceAsync(cancellationToken);
        }

        public Task<(Transaction tx, Error error)> GetTransactionAsync(
            string txId,
            CancellationToken cancellationToken = default)
        {
            return new EtherScanApi(_settings.EtherScan)
                .GetTransactionAsync(txId, cancellationToken);
        }

        public Task<(IEnumerable<EthereumTransaction> txs, Error error)> GetTransactionsAsync(
            string address,
            DateTimeOffset fromTimeStamp,
            CancellationToken cancellationToken = default)
        {
            return new EtherScanApi(_settings.EtherScan)
                .GetTransactionsAsync(address, fromTimeStamp, cancellationToken);
        }

        public Task<(IEnumerable<EthereumTransaction> txs, Error error)> GetTransactionsAsync(
            string address,
            ulong fromBlock = 0,
            ulong toBlock = ulong.MaxValue,
            CancellationToken cancellationToken = default)
        {
            return new EtherScanApi(_settings.EtherScan)
                .GetTransactionsAsync(address, fromBlock, toBlock, cancellationToken);
        }

        public Task<(long? count, Error error)> GetTransactionsCountAsync(
            string address,
            bool pending = false,
            CancellationToken cancellationToken = default)
        {
            return new EtherScanApi(_settings.EtherScan)
                .GetTransactionsCountAsync(address, pending, cancellationToken);
        }
    }
}