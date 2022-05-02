using System.Threading;
using System.Threading.Tasks;

using Atomex.Blockchain.Abstract;
using Atomex.Common;
using Atomex.Core;

namespace Atomex.Blockchain
{
    public abstract class BlockchainApi_OLD : IBlockchainApi_OLD
    {
        public abstract Task<Result<decimal>> GetBalanceAsync(
            string address,
            CancellationToken cancellationToken = default);

        public async Task<Result<decimal>> TryGetBalanceAsync(
            string address,
            int attempts = 3,
            int attemptsIntervalMs = 1000,
            CancellationToken cancellationToken = default)
        {
            return await ResultHelper.TryDo((c) => GetBalanceAsync(address, c), attempts, attemptsIntervalMs, cancellationToken)
                .ConfigureAwait(false) ?? new Error(Errors.RequestError, $"Connection error while getting balance after {attempts} attempts");
        }

        public abstract Task<Result<IBlockchainTransaction_OLD>> GetTransactionAsync(
            string txId,
            CancellationToken cancellationToken = default);

        public async Task<Result<IBlockchainTransaction_OLD>> TryGetTransactionAsync(
            string txId,
            int attempts = 3,
            int attemptsIntervalMs = 1000,
            CancellationToken cancellationToken = default)
        {
            return await ResultHelper.TryDo((c) => GetTransactionAsync(txId, c), attempts, attemptsIntervalMs, cancellationToken)
                .ConfigureAwait(false) ?? new Error(Errors.RequestError, $"Connection error while getting transaciton after {attempts} attempts");
        }

        public abstract Task<Result<string>> BroadcastAsync(
            IBlockchainTransaction_OLD transaction,
            CancellationToken cancellationToken = default);

        public async Task<Result<string>> TryBroadcastAsync(
            IBlockchainTransaction_OLD transaction,
            int attempts = 3,
            int attemptsIntervalMs = 1000,
            CancellationToken cancellationToken = default)
        {
            return await ResultHelper.TryDo((c) => BroadcastAsync(transaction, c), attempts, attemptsIntervalMs, cancellationToken)
                .ConfigureAwait(false) ?? new Error(Errors.RequestError, $"Connection error while getting transaction after {attempts} attempts");
        }
    }
}