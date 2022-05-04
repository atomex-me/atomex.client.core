using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;

using Atomex.Blockchain.Abstract;
using Atomex.Blockchain.Bitcoin.Abstract;
using Atomex.Common;

namespace Atomex.Blockchain.Bitcoin
{
    public class BitcoinApiSettings
    {
    }

    public class BitcoinApi : IBitcoinApi
    {
        public BitcoinApi(
            BitcoinApiSettings settings,
            ILogger logger = null)
        {
        }

        public Task<(string txId, Error error)> BroadcastAsync(
            Transaction transaction,
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

        public Task<(IEnumerable<BitcoinTxOutput> outputs, Error error)> GetOutputsAsync(
            string address,
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
    }
}