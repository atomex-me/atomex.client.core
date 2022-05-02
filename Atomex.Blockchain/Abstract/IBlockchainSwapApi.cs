using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Atomex.Common;

namespace Atomex.Blockchain.Abstract
{
    public interface IBlockchainSwapApi
    {
        Task<(IEnumerable<Transaction> txs, Error error)> FindLocksAsync(
            string secretHash,
            string contractAddress,
            string address,
            string refundAddress,
            ulong timeStamp,
            ulong lockTime,
            int secretSize,
            string version = null,
            CancellationToken cancellationToken = default);

        Task<(IEnumerable<Transaction> txs, Error error)> FindAdditionalLocksAsync(
            string secretHash,
            string contractAddress,
            ulong timeStamp,
            string version = null,
            CancellationToken cancellationToken = default);

        Task<(IEnumerable<Transaction> txs, Error error)> FindRedeemsAsync(
            string secretHash,
            string contractAddress,
            string address,
            string refundAddress,
            ulong timeStamp,
            ulong lockTime,
            int secretSize,
            string version = null,
            CancellationToken cancellationToken = default);

        Task<(IEnumerable<Transaction> txs, Error error)> FindRefundsAsync(
            string secretHash,
            string contractAddress,
            string address,
            string refundAddress,
            ulong timeStamp,
            ulong lockTime,
            int secretSize,
            string version = null,
            CancellationToken cancellationToken = default);
    }
}