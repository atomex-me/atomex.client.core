using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;

using Atomex.Blockchain.Abstract;
using Atomex.Blockchain.Tezos.Abstract;
using Atomex.Blockchain.Tezos.Common;
using Atomex.Blockchain.Tezos.Tzkt;
using Atomex.Common;

namespace Atomex.Blockchain.Tezos
{
    public class TezosApiSettings
    {
        public TzktSettings Tzkt { get; set; }
        public TezosRpcSettings Rpc { get; set; }
    }

    public class TezosApi : ITezosApi
    {
        private readonly TezosApiSettings _settings;
        private readonly ILogger _logger;

        public TezosApi(
            TezosApiSettings settings,
            ILogger logger = null)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _logger = logger;
        }

        public async Task<(string txId, Error error)> BroadcastAsync(
            Transaction transaction,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var rpc = new TezosRpc(_settings.Rpc);

                var operation = transaction as TezosOperation;

                var forgedOperationBytes = await operation
                    .ForgeAsync()
                    .ConfigureAwait(false);

                var signedBytesInHex = forgedOperationBytes.ToHexString()
                    + operation.Signature.ToHexString();

                var txId = await rpc
                    .InjectOperationsAsync(signedBytesInHex, cancellationToken)
                    .ConfigureAwait(false);

                return (txId, error: null);
            }
            catch (Exception e)
            {
                return (
                    txId: null,
                    error: new Error(Errors.BroadcastError, e.Message));
            }
        }

        public Task<(TezosAccount account, Error error)> GetAccountAsync(
            string address,
            CancellationToken cancellationToken = default)
        {
            return new TzktApi(_settings.Tzkt)
                .GetAccountAsync(address, cancellationToken);
        }

        public Task<(BigInteger balance, Error error)> GetBalanceAsync(
            string address,
            CancellationToken cancellationToken = default)
        {
            return new TzktApi(_settings.Tzkt)
                .GetBalanceAsync(address, cancellationToken);
        }

        public Task<(int? counter, Error error)> GetCounterAsync(
            string address,
            CancellationToken cancellationToken = default)
        {
            return new TzktApi(_settings.Tzkt)
                .GetCounterAsync(address, cancellationToken);
        }

        public Task<(string hash, Error error)> GetHeaderAsync(
            int offset = 0,
            CancellationToken cancellationToken = default)
        {
            if (offset > 0)
                throw new NotImplementedException("GetHeaderAsync with offset > 0 not implemented yet");

            return new TzktApi(_settings.Tzkt).GetHeaderAsync(cancellationToken);
        }

        public Task<(IEnumerable<TezosOperation> ops, Error error)> GetOperationsAsync(
            string address,
            DateTimeOffset fromTimeStamp,
            CancellationToken cancellationToken = default)
        {
            return new TzktApi(_settings.Tzkt)
                .GetOperationsAsync(address, fromTimeStamp, cancellationToken);
        }

        public Task<(Transaction tx, Error error)> GetTransactionAsync(
            string txId,
            CancellationToken cancellationToken = default)
        {
            return new TzktApi(_settings.Tzkt)
                .GetTransactionAsync(txId, cancellationToken);
        }

        public Task<(bool isRevealed, Error error)> IsRevealedAsync(
            string address,
            CancellationToken cancellationToken = default)
        {
            return new TzktApi(_settings.Tzkt)
                .IsRevealedAsync(address, cancellationToken);
        }

        public async Task<(string result, Error error)> RunOperationsAsync(
            string branch,
            string chainId,
            string operations,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var rpc = new TezosRpc(_settings.Rpc);

                var runResult = await rpc
                    .RunOperationsAsync(branch, chainId, operations, cancellationToken)
                    .ConfigureAwait(false);

                return (result: runResult, error: null);
            }
            catch (Exception e)
            {
                return (
                    result: null,
                    error: new Error(Errors.RunOperationsError, e.Message));
            }
        }
    }
}