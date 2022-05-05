using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;

using Atomex.Blockchain.Abstract;
using Atomex.Blockchain.Tezos.Abstract;
using Atomex.Blockchain.Tezos.Common;
using Atomex.Common;

namespace Atomex.Blockchain.Tezos
{
    public class TezosApiSettings
    {
    }

    public class TezosApi : ITezosApi
    {
        public TezosApi(
            TezosApiSettings settings,
            ILogger logger = null)
        {
        }

        public Task<(string txId, Error error)> BroadcastAsync(
            Transaction transaction,
            CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public Task<(TezosAccount account, Error error)> GetAccountAsync(
            string address,
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

        public Task<(int? counter, Error error)> GetCounterAsync(
            string address,
            CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public Task<(string hash, Error error)> GetHeaderAsync(
            int offset = 0,
            CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public Task<(IEnumerable<TezosOperation> ops, Error error)> GetOperationsAsync(
            string address,
            DateTimeOffset fromTimeStamp,
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

        public Task<(bool isRevealed, Error error)> IsRevealedAsync(
            string address,
            CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public Task<(string result, Error error)> RunOperationsAsync(
            string branch,
            string chainId,
            string operations,
            CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }
    }
}