using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Atomix.Blockchain.Abstract
{
    public interface IInOutBlockchainApi : IBlockchainApi
    {
        Task<ITxPoint> GetInputAsync(
            string txId,
            uint inputNo,
            CancellationToken cancellationToken = default(CancellationToken));

        Task<IEnumerable<ITxPoint>> GetInputsAsync(
            string txId,
            CancellationToken cancellationToken = default(CancellationToken));

        Task<IEnumerable<ITxOutput>> GetUnspentOutputsAsync(
            string address,
            string afterTxId = null,
            CancellationToken cancellationToken = default(CancellationToken));

        Task<IEnumerable<ITxOutput>> GetOutputsAsync(
            string address,
            string afterTxId = null,
            CancellationToken cancellationToken = default(CancellationToken));

        Task<ITxPoint> IsTransactionOutputSpent(
            string txId,
            uint outputNo,
            CancellationToken cancellationToken = default(CancellationToken));

        //Task<IEnumerable<ITxOutput>> GetReceivedOutputsAsync(
        //    string address,
        //    string afterTxId = null,
        //    CancellationToken cancellationToken = default(CancellationToken));

        //Task<IEnumerable<ITxOutput>> GetSpentOutputsAsync(
        //    string address,
        //    string afterTxId = null,
        //    CancellationToken cancellationToken = default(CancellationToken));
    }
}