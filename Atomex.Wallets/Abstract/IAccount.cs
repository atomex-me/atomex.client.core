using System.Threading;
using System.Threading.Tasks;

using Atomex.Common;

namespace Atomex.Wallets.Abstract
{
    public interface IAccount
    {
        string Currency { get; }
        IWalletDataRepository DataRepository { get; }

        #region Balances

        IWalletScanner GetWalletScanner();

        Task<Balance> GetBalanceAsync(
            CancellationToken cancellationToken = default);

        Task<Balance> GetWalletBalanceAsync(
            int walletId,
            CancellationToken cancellationToken = default);

        Task<Balance> GetAddressBalanceAsync(
            string address,
            CancellationToken cancellationToken = default);

        Task<(Balance balance, Error error)> UpdateBalanceAsync(
            bool skipUsedAddresses = true,
            CancellationToken cancellationToken = default);

        Task<(Balance balance, Error error)> UpdateWalletBalanceAsync(
            int walletId,
            bool skipUsedAddresses = true,
            CancellationToken cancellationToken = default);

        Task<(Balance balance, Error error)> UpdateAddressBalanceAsync(
            string address,
            CancellationToken cancellationToken = default);

        #endregion Balances

        #region Addresses

        Task<WalletAddress> GetFreeAddressAsync(
            int walletId,
            CancellationToken cancellationToken = default);

        #endregion Addresses
    }
}