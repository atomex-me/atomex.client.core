using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Atomex.Common;

namespace Atomex.Blockchain.Abstract
{
    public interface IInOutBlockchainApi : IBlockchainApi
    {
        Task<Result<ITxPoint>> GetInputAsync(
            string txId,
            uint inputNo,
            CancellationToken cancellationToken = default(CancellationToken));

        Task<Result<IEnumerable<ITxOutput>>> GetUnspentOutputsAsync(
            string address,
            string afterTxId = null,
            CancellationToken cancellationToken = default(CancellationToken));

        Task<Result<IEnumerable<ITxOutput>>> GetOutputsAsync(
            string address,
            string afterTxId = null,
            CancellationToken cancellationToken = default(CancellationToken));

        Task<Result<ITxPoint>> IsTransactionOutputSpent(
            string txId,
            uint outputNo,
            CancellationToken cancellationToken = default(CancellationToken));
    }
}