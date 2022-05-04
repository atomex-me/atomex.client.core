using System.Threading;
using System.Threading.Tasks;

using Atomex.Common;

namespace Atomex.Wallets.Abstract
{
    public interface IWalletScanner
    {
        /// <summary>
        /// Update balances for all account's wallets
        /// </summary>
        /// <param name="forceUpdate">If flag is set, address usage policy will be ignored</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Null if success, otherwise error</returns>
        Task<Error> UpdateBalanceAsync(
            bool forceUpdate = false,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Update balances for wallet with id <paramref name="walletId"/>
        /// </summary>
        /// <param name="walletId">Wallet ID</param>
        /// <param name="forceUpdate">If flag is set, address usage policy will be ignored</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Null if success, otherwise error</returns>
        Task<Error> UpdateBalanceAsync(
            int walletId,
            bool forceUpdate = false,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Update address balance. Address must be stored in local db
        /// </summary>
        /// <param name="address">Address to update</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Null if success, otherwise error</returns>
        Task<Error> UpdateAddressBalanceAsync(
            string address,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Scan balances for all account's wallets
        /// </summary>
        /// <param name="forceUpdate">If flag is set, address usage policy will be ignored</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Null if success, otherwise error</returns>
        Task<Error> ScanAsync(
            bool forceUpdate = false,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Scan balances for wallet with id <paramref name="walletId"/>
        /// </summary>
        /// <param name="walletId">Wallet ID</param>
        /// <param name="forceUpdate">If flag is set, address usage policy will be ignored</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Null if success, otherwise error</returns>
        Task<Error> ScanAsync(
            int walletId,
            bool forceUpdate = false,
            CancellationToken cancellationToken = default);
    }
}