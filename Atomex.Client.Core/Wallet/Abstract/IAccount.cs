using System;
using System.Collections.Generic;
using System.Numerics;
using System.Security;
using System.Threading;
using System.Threading.Tasks;

using Atomex.Abstract;
using Atomex.Blockchain.Abstract;
using Atomex.Common;
using Atomex.Core;

namespace Atomex.Wallet.Abstract
{
    public interface IAccount : IAddressResolver
    {
        event EventHandler Locked;
        event EventHandler Unlocked;

        bool IsLocked { get; }

        /// <summary>
        /// Wallet's network
        /// </summary>
        Network Network { get; }

        /// <summary>
        /// Wallet
        /// </summary>
        IHdWallet Wallet { get; }

        /// <summary>
        /// Get all currencies supported by wallet
        /// </summary>
        ICurrencies Currencies { get; }

        /// <summary>
        /// Get user settings
        /// </summary>
        UserData UserData { get; }
        
        /// <summary>
        /// Get user settings file path
        /// </summary>
        string SettingsFilePath { get; }

        #region Common

        bool ChangePassword(SecureString newPassword);

        /// <summary>
        /// Lock account wallet
        /// </summary>
        void Lock();

        /// <summary>
        /// Unlock account wallet using <paramref name="password"/>
        /// </summary>
        /// <param name="password">Password</param>
        void Unlock(SecureString password);

        /// <summary>
        /// Use user settings
        /// </summary>
        /// <param name="userData">User settings</param>
        /// <returns>this</returns>
        IAccount UseUserSettings(UserData userData);

        ICurrencyAccount GetCurrencyAccount(
            string currency,
            string? tokenContract = null,
            BigInteger? tokenId = null);

        T GetCurrencyAccount<T>(
            string currency,
            string? tokenContract = null,
            BigInteger? tokenId = null) where T : class, ICurrencyAccount;

        string GetUserId(uint keyIndex = 0);

        #endregion

        #region Balances

        Task<Balance> GetBalanceAsync(
            string currency,
            string? tokenContract = null,
            BigInteger? tokenId = null,
            CancellationToken cancellationToken = default);

        Task<Balance> GetAddressBalanceAsync(
            string currency,
            string address,
            string? tokenContract = null,
            BigInteger? tokenId = null,
            CancellationToken cancellationToken = default);

        Task UpdateBalanceAsync(
            string currency,
            string? tokenContract = null,
            BigInteger? tokenId = null,
            CancellationToken cancellationToken = default);

        Task UpdateBalanceAsync(
            string currency,
            string address,
            string? tokenContract = null,
            BigInteger? tokenId = null,
            CancellationToken cancellationToken = default);

        #endregion Balances

        #region Addresses

        /// <summary>
        /// Gets unspent addresses for <paramref name="currency"/>
        /// </summary>
        /// <param name="currency">Currency</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Set of unspent addresses</returns>
        Task<IEnumerable<WalletAddress>> GetUnspentAddressesAsync(
            string currency,
            string? tokenContract = null,
            BigInteger? tokenId = null,
            CancellationToken cancellationToken = default);
        
        /// <summary>
        /// Gets free external address for <paramref name="currency"/>
        /// </summary>
        /// <param name="currency">Currency</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Wallet address</returns>
        Task<WalletAddress> GetFreeExternalAddressAsync(
            string currency,
            string? tokenContract = null,
            BigInteger? tokenId = null,
            CancellationToken cancellationToken = default);

        #endregion Addresses

        #region Transactions

        Task<IEnumerable<TransactionInfo<ITransaction, ITransactionMetadata>>> GetTransactionsWithMetadataAsync(
            string currency,
            int offset = 0,
            int limit = int.MaxValue,
            SortDirection sort = SortDirection.Desc,
            CancellationToken cancellationToken = default);
        Task<IEnumerable<ITransaction>> GetUnconfirmedTransactionsAsync();
        Task<ITransactionMetadata> ResolveTransactionMetadataAsync(
            ITransaction tx,
            CancellationToken cancellationToken = default);

        #endregion Transactions

        #region Orders
        Order GetOrderById(long id);

        #endregion Orders

        #region Swaps

        Task<bool> AddSwapAsync(Swap swap);
        Task<bool> UpdateSwapAsync(Swap swap);
        Task<Swap> GetSwapByIdAsync(long id);
        Task<IEnumerable<Swap>> GetSwapsAsync();

        #endregion Swaps
    }
}