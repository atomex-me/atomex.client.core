using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;

using Atomex.Blockchain.Abstract;
using Atomex.Blockchain.Bitcoin.Abstract;
using Atomex.Blockchain.Bitcoin.SoChain;
using Atomex.Common;

namespace Atomex.Blockchain.Bitcoin
{
    public class BitcoinApiSettings
    {
        public SoChainSettings SoChain { get; set; }
    }

    public class BitcoinApi : IBitcoinApi
    {
        private readonly string _currency;
        private readonly BitcoinApiSettings _settings;
        private readonly ILogger _logger;

        public BitcoinApi(
            string currency,
            BitcoinApiSettings settings,
            ILogger logger = null)
        {
            _currency = currency ?? throw new ArgumentNullException(nameof(currency));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _logger = logger;
        }

        public Task<(string txId, Error error)> BroadcastAsync(
            Transaction transaction,
            CancellationToken cancellationToken = default)
        {
            return new SoChainApi(_currency, _settings.SoChain)
                .BroadcastAsync(transaction, cancellationToken);
        }

        public Task<(BigInteger balance, Error error)> GetBalanceAsync(
            string address,
            CancellationToken cancellationToken = default)
        {
            return new SoChainApi(_currency, _settings.SoChain)
                .GetBalanceAsync(address, cancellationToken);
        }

        public Task<(IEnumerable<BitcoinTxOutput> outputs, Error error)> GetOutputsAsync(
            string address,
            CancellationToken cancellationToken = default)
        {
            return new SoChainApi(_currency, _settings.SoChain)
                .GetOutputsAsync(address, cancellationToken);
        }

        public Task<(Transaction tx, Error error)> GetTransactionAsync(
            string txId,
            CancellationToken cancellationToken = default)
        {
            return new SoChainApi(_currency, _settings.SoChain)
                .GetTransactionAsync(txId, cancellationToken);
        }
    }
}