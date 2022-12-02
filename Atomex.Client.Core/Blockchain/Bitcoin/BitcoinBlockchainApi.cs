using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Atomex.Blockchain.Abstract;
using Atomex.Common;

namespace Atomex.Blockchain.Bitcoin
{
    public record BitcoinAddressInfo(
        decimal Balance,
        decimal Received,
        decimal Sent,
        decimal UnconfirmedIncome,
        decimal UnconfirmedOutcome,
        IEnumerable<BitcoinTxOutput> Outputs);

    public abstract class BitcoinBlockchainApi : BlockchainApi
    {
        public abstract Task<Result<ITxPoint>> GetInputAsync(
            string txId,
            uint inputNo,
            CancellationToken cancellationToken = default);

        public abstract Task<Result<IEnumerable<BitcoinTxOutput>>> GetOutputsAsync(
            string address,
            string afterTxId = null,
            CancellationToken cancellationToken = default);

        public abstract Task<Result<IEnumerable<BitcoinTxOutput>>> GetUnspentOutputsAsync(
            string address,
            string afterTxId = null,
            CancellationToken cancellationToken = default);

        public abstract Task<Result<ITxPoint>> IsTransactionOutputSpent(
            string txId,
            uint outputNo,
            CancellationToken cancellationToken = default);

        public abstract Task<Result<BitcoinAddressInfo>> GetAddressInfo(
            string address,
            CancellationToken cancellationToken = default);
    }
}