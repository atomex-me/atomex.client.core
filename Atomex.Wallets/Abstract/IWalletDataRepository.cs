using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Atomex.Blockchain.Abstract;

namespace Atomex.Wallets.Abstract
{
    public interface IWalletDataRepository
    {
        #region Wallets

        Task<bool> UpsertWalletInfoAsync(
            WalletInfo walletInfo,
            CancellationToken cancellationToken = default);

        Task<WalletInfo> GetWalletInfoByIdAsync(
            int walletId,
            CancellationToken cancellationToken = default);

        Task<IEnumerable<WalletInfo>> GetWalletsInfoAsync(
            CancellationToken cancellationToken = default);

        Task<IEnumerable<WalletInfo>> GetWalletsInfoAsync(
            string currency,
            CancellationToken cancellationToken = default);

        Task<bool> RemoveWalletInfoAsync(
            int walletId,
            CancellationToken cancellationToken = default);

        #endregion Wallets

        #region Addresses

        Task<bool> UpsertAddressAsync(
            WalletAddress walletAddress,
            CancellationToken cancellationToken = default);

        Task<int> UpsertAddressesAsync(
            IEnumerable<WalletAddress> walletAddresses,
            CancellationToken cancellationToken = default);

        Task<bool> TryInsertAddressAsync(
            WalletAddress walletAddress,
            CancellationToken cancellationToken = default);

        Task<WalletAddress> GetWalletAddressAsync(
            string currency,
            string address,
            CancellationToken cancellationToken = default);

        Task<WalletAddress> GetLastActiveWalletAddressAsync(
            string currency,
            int walletId,
            string keyPathPattern,
            CancellationToken cancellationToken = default);

        Task<IEnumerable<WalletAddress>> GetAddressesAsync(
            string currency,
            int offset = 0,
            int limit = int.MaxValue,
            CancellationToken cancellationToken = default);

        Task<IEnumerable<WalletAddress>> GetAddressesAsync(
            string currency,
            int walletId,
            int offset = 0,
            int limit = int.MaxValue,
            CancellationToken cancellationToken = default);

        Task<IEnumerable<WalletAddress>> GetUnspentAddressesAsync(
            string currency,
            CancellationToken cancellationToken = default);

        Task<IEnumerable<WalletAddress>> GetUnspentAddressesAsync(
            string currency,
            int walletId,
            CancellationToken cancellationToken = default);

        #endregion Addresses

        #region Transactions

        Task<bool> UpsertTransactionAsync<T>(
            T tx,
            CancellationToken cancellationToken = default)
            where T : Transaction;

        Task<int> UpsertTransactionsAsync<T>(
            IEnumerable<T> txs,
            CancellationToken cancellationToken = default)
            where T : Transaction;

        Task<T> GetTransactionByIdAsync<T>(
            string currency,
            string txId,
            CancellationToken cancellationToken = default)
            where T : Transaction;

        Task<IEnumerable<T>> GetTransactionsAsync<T>(
            string currency,
            int offset = 0,
            int limit = int.MaxValue,
            CancellationToken cancellationToken = default)
            where T : Transaction;

        Task<IEnumerable<T>> GetUnconfirmedTransactionsAsync<T>(
            string currency,
            int offset = 0,
            int limit = int.MaxValue,
            CancellationToken cancellationToken = default)
            where T : Transaction;

        Task<bool> RemoveTransactionByIdAsync(
            string currency,
            string txId,
            CancellationToken cancellationToken = default);

        #endregion Transactions
    }
}