using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Atomex.Blockchain.Abstract;
using Atomex.Common;

namespace Atomex.Wallets.Abstract
{
    public interface IAccount
    {
        string Currency { get; }

        //#region Wallets

        //Task<WalletInfo> GetWalletByIdAsync(
        //    int walletId,
        //    CancellationToken cancellationToken);

        //Task<IEnumerable<WalletInfo>> GetWalletsAsync(
        //    CancellationToken cancellationToken = default);

        //Task<IEnumerable<WalletInfo>> GetWalletsAsync(
        //    string currency,
        //    CancellationToken cancellationToken = default);

        //#endregion Wallets

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

        //Task<bool> UpsertAddressAsync(
        //    WalletAddress walletAddress,
        //    CancellationToken cancellationToken = default);

        Task<WalletAddress> GetAddressAsync(
            string address,
            CancellationToken cancellationToken = default);

        Task<IEnumerable<WalletAddress>> GetAddressesAsync(
            int walletId,
            int offset = 0,
            int limit = int.MaxValue,
            CancellationToken cancellationToken = default);

        Task<WalletAddress> GetFreeAddressAsync(
            int walletId,
            CancellationToken cancellationToken = default);

        #endregion Addresses

        #region Transactions

        //Task<bool> UpsertTransactionAsync<T>(
        //    T tx,
        //    CancellationToken cancellationToken = default)
        //    where T : Transaction;

        //Task<int> UpsertTransactionsAsync<T>(
        //    IEnumerable<T> txs,
        //    CancellationToken cancellationToken = default)
        //    where T : Transaction;

        Task<T> GetTransactionByIdAsync<T>(
            string txId,
            CancellationToken cancellationToken = default)
            where T : Transaction;

        Task<IEnumerable<T>> GetUnconfirmedTransactionsAsync<T>(
            int offset = 0,
            int limit = int.MaxValue,
            CancellationToken cancellationToken = default)
            where T : Transaction;

        Task<bool> RemoveTransactionByIdAsync(
            string txId,
            CancellationToken cancellationToken = default);

        #endregion Transactions
    }
}