using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Atomex.Blockchain.Abstract;
using Atomex.Common;
using Atomex.Core;

namespace Atomex.Blockchain.BitcoinBased
{
    public abstract class BitcoinBasedBlockchainApi : BlockchainApi, IInOutBlockchainApi
    {
        public abstract Task<Result<ITxPoint>> GetInputAsync(
            string txId,
            uint inputNo,
            CancellationToken cancellationToken = default);

        public async Task<Result<ITxPoint>> TryGetInputAsync(
            string txId,
            uint inputNo,
            int attempts = 10,
            int attemptsIntervalMs = 1000,
            CancellationToken cancellationToken = default)
        {
            return await ResultHelper.TryDo((c) => GetInputAsync(txId, inputNo, c), attempts, attemptsIntervalMs, cancellationToken)
                ?? new Error(Errors.RequestError, $"Connection error while getting input after {attempts} attempts");
        }

        public abstract Task<Result<IEnumerable<ITxOutput>>> GetOutputsAsync(
            string address,
            string afterTxId = null,
            CancellationToken cancellationToken = default);

        public async Task<Result<IEnumerable<ITxOutput>>> TryGetOutputsAsync(
            string address,
            string afterTxId = null,
            int attempts = 10,
            int attemptsIntervalMs = 1000,
            CancellationToken cancellationToken = default)
        {
            return await ResultHelper.TryDo((c) => GetOutputsAsync(address, afterTxId, c), attempts, attemptsIntervalMs, cancellationToken)
                ?? new Error(Errors.RequestError, $"Connection error while getting outputs after {attempts} attempts");
        }

        public abstract Task<Result<IEnumerable<ITxOutput>>> GetUnspentOutputsAsync(
            string address,
            string afterTxId = null,
            CancellationToken cancellationToken = default);

        public abstract Task<Result<ITxPoint>> IsTransactionOutputSpent(
            string txId,
            uint outputNo,
            CancellationToken cancellationToken = default);

        public async Task<Result<ITxPoint>> TryIsTransactionOutputSpent(
            string txId,
            uint outputNo,
            int attempts = 10,
            int attemptsIntervalMs = 1000,
            CancellationToken cancellationToken = default)
        {
            return await ResultHelper.TryDo((c) => IsTransactionOutputSpent(txId, outputNo, c), attempts, attemptsIntervalMs, cancellationToken)
                ?? new Error(Errors.RequestError, $"Connection error while getting transaction output after {attempts} attempts");
        }
    }
}