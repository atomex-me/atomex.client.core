using System;
using System.Collections.Generic;
using System.Security;
using System.Threading;
using System.Threading.Tasks;

using Atomex.Abstract;
using Atomex.Api;
using Atomex.Blockchain;
using Atomex.Blockchain.Abstract;
using Atomex.Core;

namespace Atomex.Wallet.Abstract
{
    public interface IAccount_OLD : IAddressResolver
    {
        event EventHandler<CurrencyEventArgs> BalanceUpdated;
        event EventHandler<TransactionEventArgs> UnconfirmedTransactionAdded;
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
        IHdWallet_OLD Wallet { get; }

        /// <summary>
        /// Get all currencies supported by wallet
        /// </summary>
        ICurrencies Currencies { get; }

        /// <summary>
        /// Get user settings
        /// </summary>
        UserSettings UserSettings { get; }
        
        /// <summary>
        /// Get user settings file path
        /// </summary>
        string SettingsFilePath { get; }

        #region Common

        void ChangePassword(SecureString newPassword);

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
        /// <param name="userSettings">User settings</param>
        /// <returns>this</returns>
        IAccount_OLD UseUserSettings(UserSettings userSettings);

        /// <summary>
        /// Create auth request for service key with <paramref name="keyIndex"/>, which can be used for authentication using server <paramref name="nonce"/>
        /// </summary>
        /// <param name="nonce">Server nonce</param>
        /// <param name="keyIndex">Service key index</param>
        /// <returns>Auth request</returns>
        Task<Auth> CreateAuthRequestAsync(
            AuthNonce nonce,
            uint keyIndex = 0);

        ICurrencyAccount_OLD GetCurrencyAccount(string currency);

        ICurrencyAccount_OLD GetTezosTokenAccount(
            string currency,
            string tokenContract,
            decimal tokenId);

        T GetCurrencyAccount<T>(string currency) where T : class, ICurrencyAccount_OLD;

        T GetTezosTokenAccount<T>(
            string currency,
            string tokenContract,
            decimal tokenId) where T : class;

        string GetUserId(uint keyIndex = 0);

        #endregion

        #region Balances

        Task<Balance_OLD> GetBalanceAsync(
            string currency,
            CancellationToken cancellationToken = default);

        Task<Balance_OLD> GetAddressBalanceAsync(
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
        Task<IEnumerable<WalletAddress_OLD>> GetUnspentAddressesAsync(
            string currency,
            CancellationToken cancellationToken = default);
        
        /// <summary>
        /// Gets free external address for <paramref name="currency"/>
        /// </summary>
        /// <param name="currency">Currency</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Wallet address</returns>
        Task<WalletAddress_OLD> GetFreeExternalAddressAsync(
            string currency,
            CancellationToken cancellationToken = default);

        #endregion Addresses

        #region Transactions

        Task<IBlockchainTransaction_OLD> GetTransactionByIdAsync(
            string currency,
            string txId);

        Task<IEnumerable<IBlockchainTransaction_OLD>> GetTransactionsAsync(
            string currency);

        Task<IEnumerable<IBlockchainTransaction_OLD>> GetTransactionsAsync();

        Task<bool> RemoveTransactionAsync(
            string id);

        #endregion Transactions

        #region Orders

        Task<bool> UpsertOrderAsync(Order order);
        Order GetOrderById(string clientOrderId);
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