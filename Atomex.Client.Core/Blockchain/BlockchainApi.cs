using System.Threading;
using System.Threading.Tasks;
using Atomex.Blockchain.Abstract;
using Atomex.Common;
using Atomex.Core;

namespace Atomex.Blockchain
{
    public abstract class BlockchainApi : IBlockchainApi
    {
        public abstract Task<Result<decimal>> GetBalanceAsync(
            string address,
            CancellationToken cancellationToken = default);

        public async Task<Result<decimal>> TryGetBalanceAsync(
            string address,
            int attempts = 10,
            int attemptsIntervalMs = 1000,
            CancellationToken cancellationToken = default)
        {
            return await ResultHelper.TryDo((c) => GetBalanceAsync(address, c), attempts, attemptsIntervalMs, cancellationToken)
                ?? new Error(Errors.RequestError, $"Connection error while getting balance after {attempts} attempts");
        }

        public abstract Task<Result<IBlockchainTransaction>> GetTransactionAsync(
            string txId,
            CancellationToken cancellationToken = default);

        public async Task<Result<IBlockchainTransaction>> TryGetTransactionAsync(
            string txId,
            int attempts = 10,
            int attemptsIntervalMs = 1000,
            CancellationToken cancellationToken = default)
        {
            return await ResultHelper.TryDo((c) => GetTransactionAsync(txId, c), attempts, attemptsIntervalMs, cancellationToken)
                ?? new Error(Errors.RequestError, $"Connection error while getting transaciton after {attempts} attempts");
        }

        public abstract Task<Result<string>> BroadcastAsync(
            IBlockchainTransaction transaction,
            CancellationToken cancellationToken = default);

        public async Task<Result<string>> TryBroadcastAsync(
            IBlockchainTransaction transaction,
            int attempts = 10,
            int attemptsIntervalMs = 1000,
            CancellationToken cancellationToken = default)
        {
            return await ResultHelper.TryDo((c) => BroadcastAsync(transaction, c), attempts, attemptsIntervalMs, cancellationToken)
                ?? new Error(Errors.RequestError, $"Connection error while getting transaciton after {attempts} attempts");
        }
    }
}