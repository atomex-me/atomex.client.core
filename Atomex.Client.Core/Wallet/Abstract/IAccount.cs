using System;
using System.Collections.Generic;
using System.Security;
using System.Threading;
using System.Threading.Tasks;

using Atomex.Abstract;
using Atomex.Blockchain.Abstract;
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

        /// <summary>
        /// Create auth request for service key with <paramref name="keyIndex"/>, which can be used for authentication using server <paramref name="nonce"/>
        /// </summary>
        /// <param name="nonce">Server nonce</param>
        /// <param name="keyIndex">Service key index</param>
        /// <returns>Auth request</returns>
        //Task<Auth> CreateAuthRequestAsync(
        //    AuthNonce nonce,
        //    uint keyIndex = 0);

        ICurrencyAccount GetCurrencyAccount(string currency);

        ICurrencyAccount GetTezosTokenAccount(
            string currency,
            string tokenContract,
            int tokenId);

        T GetCurrencyAccount<T>(string currency) where T : class, ICurrencyAccount;

        T GetTezosTokenAccount<T>(
            string currency,
            string tokenContract,
            int tokenId) where T : class;

        string GetUserId(uint keyIndex = 0);

        #endregion

        #region Balances

        Task<Balance> GetBalanceAsync(
            string currency,
            CancellationToken cancellationToken = default);

        Task<Balance> GetAddressBalanceAsync(
            string currency,
            string address,
            CancellationToken cancellationToken = default);

        Task UpdateBalanceAsync(
            string currency,
            CancellationToken cancellationToken = default);

        Task UpdateBalanceAsync(
            string currency,
            string address,
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
            CancellationToken cancellationToken = default);
        
        /// <summary>
        /// Gets free external address for <paramref name="currency"/>
        /// </summary>
        /// <param name="currency">Currency</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Wallet address</returns>
        Task<WalletAddress> GetFreeExternalAddressAsync(
            string currency,
            CancellationToken cancellationToken = default);

        #endregion Addresses

        #region Transactions

        Task<T> GetTransactionByIdAsync<T>(string currency, string txId)
            where T : IBlockchainTransaction;

        Task<IEnumerable<T>> GetTransactionsAsync<T>(string currency)
            where T : IBlockchainTransaction;

        Task<IEnumerable<IBlockchainTransaction>> GetUnconfirmedTransactionsAsync();

        Task<bool> RemoveTransactionAsync(string id);

        #endregion Transactions

        #region Orders

        Task<bool> UpsertOrderAsync(Order order);
        Task<bool> RemoveAllOrdersAsync();
        Order GetOrderById(string clientOrderId);
        Order GetOrderById(long id);
        Task<bool> RemoveOrderByIdAsync(long id);

        #endregion Orders

        #region Swaps

        Task<bool> AddSwapAsync(Swap swap);
        Task<bool> UpdateSwapAsync(Swap swap);
        Task<Swap> GetSwapByIdAsync(long id);
        Task<IEnumerable<Swap>> GetSwapsAsync();

        #endregion Swaps
    }
}