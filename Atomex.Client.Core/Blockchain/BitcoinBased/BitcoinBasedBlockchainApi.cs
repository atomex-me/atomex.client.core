using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Atomex.Blockchain.Abstract;
using Atomex.Common;

namespace Atomex.Blockchain.BitcoinBased
{
    public record BitcoinBasedAddressInfo(
        decimal Balance,
        decimal UnconfirmedIncome,
        decimal UnconfirmedOutcome,
        IEnumerable<BitcoinBasedTxOutput> Outputs);

    public abstract class BitcoinBasedBlockchainApi : BlockchainApi
    {
        public abstract Task<Result<ITxPoint>> GetInputAsync(
            string txId,
            uint inputNo,
            CancellationToken cancellationToken = default);

        public async Task<Result<ITxPoint>> TryGetInputAsync(
            string txId,
            uint inputNo,
            int attempts = 3,
            int attemptsIntervalMs = 1000,
            CancellationToken cancellationToken = default)
        {
            return await ResultHelper.TryDo((c) => GetInputAsync(txId, inputNo, c), attempts, attemptsIntervalMs, cancellationToken)
                .ConfigureAwait(false) ?? new Error(Errors.RequestError, $"Connection error while getting input after {attempts} attempts");
        }

        public abstract Task<Result<IEnumerable<BitcoinBasedTxOutput>>> GetOutputsAsync(
            string address,
            string afterTxId = null,
            CancellationToken cancellationToken = default);

        public async Task<Result<IEnumerable<BitcoinBasedTxOutput>>> TryGetOutputsAsync(
            string address,
            string afterTxId = null,
            int attempts = 3,
            int attemptsIntervalMs = 1000,
            CancellationToken cancellationToken = default)
        {
            return await ResultHelper.TryDo((c) => GetOutputsAsync(address, afterTxId, c), attempts, attemptsIntervalMs, cancellationToken)
                .ConfigureAwait(false) ?? new Error(Errors.RequestError, $"Connection error while getting outputs after {attempts} attempts");
        }

        public abstract Task<Result<IEnumerable<BitcoinBasedTxOutput>>> GetUnspentOutputsAsync(
            string address,
            string afterTxId = null,
            CancellationToken cancellationToken = default);

        public abstract Task<Result<ITxPoint>> IsTransactionOutputSpent(
            string txId,
            uint outputNo,
            CancellationToken cancellationToken = default);
    }
}