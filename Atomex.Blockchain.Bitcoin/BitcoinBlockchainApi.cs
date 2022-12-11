using System.Collections.Generic;
using System.Numerics;
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

    public abstract class BitcoinBlockchainApi : IBlockchainApi
    {
        public abstract Task<Result<BigInteger>> GetBalanceAsync(
            string address,
            CancellationToken cancellationToken = default);

        public abstract Task<Result<ITransaction>> GetTransactionAsync(
            string txId,
            CancellationToken cancellationToken = default);

        public abstract Task<Result<string>> BroadcastAsync(
            ITransaction transaction,
            CancellationToken cancellationToken = default);

        public abstract Task<Result<BitcoinTxInput>> GetInputAsync(
            string txId,
            uint inputNo,
            CancellationToken cancellationToken = default);

        public abstract Task<Result<IEnumerable<BitcoinTxOutput>>> GetOutputsAsync(
            string address,
            CancellationToken cancellationToken = default);

        public abstract Task<Result<IEnumerable<BitcoinTxOutput>>> GetUnspentOutputsAsync(
            string address,
            CancellationToken cancellationToken = default);

        public abstract Task<Result<BitcoinTxPoint>> IsTransactionOutputSpent(
            string txId,
            uint outputNo,
            CancellationToken cancellationToken = default);

        public abstract Task<Result<BitcoinAddressInfo>> GetAddressInfo(
            string address,
            CancellationToken cancellationToken = default);
    }
}