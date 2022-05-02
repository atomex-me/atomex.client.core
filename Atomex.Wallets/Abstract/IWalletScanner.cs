using System.Threading;
using System.Threading.Tasks;

using Atomex.Common;

namespace Atomex.Wallets.Abstract
{
    public interface IWalletScanner
    {
        /// <summary>
        /// Scan balances for all account's wallets
        /// </summary>
        /// <param name="skipUsedAddresses">If flag is set, addresses with activity and zero balance are skipped</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Null if success, otherwise error</returns>
        Task<Error> ScanAsync(
            bool skipUsedAddresses = false,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Scan balances for wallet with id <paramref name="walletId"/>
        /// </summary>
        /// <param name="walletId">Wallet ID</param>
        /// <param name="skipUsedAddresses">If flag is set, addresses with activity and zero balance are skipped</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Null if success, otherwise error</returns>
        Task<Error> ScanAsync(
            int walletId,
            bool skipUsedAddresses = false,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Scan address balance. Address must be stored in local db
        /// </summary>
        /// <param name="address">Address to scan</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Null if success, otherwise error</returns>
        Task<Error> ScanAsync(
            string address,
            CancellationToken cancellationToken = default);
    }
}