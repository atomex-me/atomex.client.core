using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Atomex.Blockchain.BitcoinBased;
using Atomex.Common;

namespace Atomex.Blockchain.Abstract
{
    public interface IInOutBlockchainApi : IBlockchainApi
    {
        Task<Result<ITxPoint>> GetInputAsync(
            string txId,
            uint inputNo,
            CancellationToken cancellationToken = default);

        Task<Result<ITxPoint>> TryGetInputAsync(
            string txId,
            uint inputNo,
            int attempts = 3,
            int attemptsIntervalMs = 1000,
            CancellationToken cancellationToken = default);

        Task<Result<IEnumerable<BitcoinBasedTxOutput>>> GetUnspentOutputsAsync(
            string address,
            string afterTxId = null,
            CancellationToken cancellationToken = default);

        Task<Result<IEnumerable<BitcoinBasedTxOutput>>> GetOutputsAsync(
            string address,
            string afterTxId = null,
            CancellationToken cancellationToken = default);

        Task<Result<IEnumerable<BitcoinBasedTxOutput>>> TryGetOutputsAsync(
            string address,
            string afterTxId = null,
            int attempts = 3,
            int attemptsIntervalMs = 1000,
            CancellationToken cancellationToken = default);

        Task<Result<ITxPoint>> IsTransactionOutputSpent(
            string txId,
            uint outputNo,
            CancellationToken cancellationToken = default);
    }
}