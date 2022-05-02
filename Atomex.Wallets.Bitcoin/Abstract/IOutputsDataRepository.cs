using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Atomex.Blockchain.Bitcoin;

namespace Atomex.Wallets.Bitcoin.Abstract
{
    public interface IOutputsDataRepository
    {
        #region Outputs

        Task<bool> UpsertOutputAsync(
            BitcoinTxOutput output,
            CancellationToken cancellationToken = default);

        Task<int> UpsertOutputsAsync(
            IEnumerable<BitcoinTxOutput> outputs,
            CancellationToken cancellationToken = default);

        Task<IEnumerable<T>> GetUnspentOutputsAsync<T>(
            string currency,
            CancellationToken cancellationToken = default)
            where T : BitcoinTxOutput;

        Task<IEnumerable<T>> GetUnspentOutputsAsync<T>(
            string currency,
            string address,
            CancellationToken cancellationToken = default)
            where T : BitcoinTxOutput;

        Task<IEnumerable<T>> GetOutputsAsync<T>(
            string currency,
            CancellationToken cancellationToken = default)
            where T : BitcoinTxOutput;

        Task<IEnumerable<T>> GetOutputsAsync<T>(
            string currency,
            string address,
            CancellationToken cancellationToken = default)
            where T : BitcoinTxOutput;

        #endregion
    }
}