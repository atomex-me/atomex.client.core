using System;
using System.Collections.Generic;
using System.Security;
using System.Threading;
using System.Threading.Tasks;
using Atomix.Blockchain;
using Atomix.Blockchain.Abstract;
using Atomix.Core;
using Atomix.Core.Abstract;
using Atomix.Core.Entities;
using Atomix.Swaps.Abstract;

namespace Atomix.Wallet.Abstract
{
    public interface IAccount : ISwapRepository, IOrderRepository, ITransactionRepository
    {
        event EventHandler<CurrencyEventArgs> BalanceUpdated;
        event EventHandler<TransactionEventArgs> UnconfirmedTransactionAdded;
        event EventHandler SwapsLoaded;
        event EventHandler Locked;
        event EventHandler Unlocked;
        bool IsLocked { get; }

        IHdWallet Wallet { get; }

        Task<Error> SendAsync(
            Currency currency,
            string to,
            decimal amount,
            decimal fee,
            decimal feePrice,
            CancellationToken cancellationToken = default(CancellationToken));

        Task<decimal> EstimateFeeAsync(Currency currency,
            decimal amount,
            CancellationToken cancellationToken = default(CancellationToken));

        Task AddUnconfirmedTransactionAsync(
            IBlockchainTransaction tx,
            string[] selfAddresses,
            bool notify = true,
            CancellationToken cancellationToken = default(CancellationToken));

        Task AddConfirmedTransactionAsync(
            IBlockchainTransaction tx,
            CancellationToken cancellationToken = default(CancellationToken));

        Task<decimal> GetBalanceAsync(
            Currency currency,
            string address,
            CancellationToken cancellationToken = default(CancellationToken));

        Task<decimal> GetBalanceAsync(
            Currency currency,
            CancellationToken cancellationToken = default(CancellationToken));

        Task<IEnumerable<IBlockchainTransaction>> GetTransactionsAsync();

        Task LoadSwapsAsync();

        #region Wallet

        /// <summary>
        /// Get all currencies supported by account wallet
        /// </summary>
        IEnumerable<Currency> Currencies { get; }

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
        /// Gets address for <paramref name="currency"/>, <paramref name="chain"/> and key <paramref name="index"/>
        /// </summary>
        /// <param name="currency">Currency</param>
        /// <param name="chain">Chain</param>
        /// <param name="index">Key index</param>
        /// <returns>Address</returns>
        WalletAddress GetAddress(
            Currency currency,
            uint chain,
            uint index);

        /// <summary>
        /// Gets unspent addresses for <paramref name="currency"/> and <paramref name="requiredAmount"/>
        /// </summary>
        /// <param name="currency">Currency</param>
        /// <param name="requiredAmount">Required amount</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Set of unspent addresses</returns>
        Task<IEnumerable<WalletAddress>> GetUnspentAddressesAsync(
            Currency currency,
            decimal requiredAmount,
            CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>
        /// Gets free internal address for <paramref name="currency"/>
        /// </summary>
        /// <param name="currency">Currency</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Wallet address</returns>
        Task<WalletAddress> GetFreeInternalAddressAsync(
            Currency currency,
            CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>
        /// Gets free external address for <paramref name="currency"/>
        /// </summary>
        /// <param name="currency">Currency</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Wallet address</returns>
        Task<WalletAddress> GetFreeExternalAddressAsync(
            Currency currency,
            CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>
        /// Get refund address for <paramref name="currency"/>
        /// </summary>
        /// <param name="currency">Currency</param>
        /// <param name="paymentAddresses">Payment addresses</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Wallet address</returns>
        Task<WalletAddress> GetRefundAddressAsync(
            Currency currency,
            IEnumerable<WalletAddress> paymentAddresses,
            CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>
        /// Get redeem address for <paramref name="currency"/>
        /// </summary>
        /// <param name="currency">Currency</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Wallet address</returns>
        Task<WalletAddress> GetRedeemAddressAsync(
            Currency currency,
            CancellationToken cancellationToken = default(CancellationToken));

        #endregion Wallet

        /// <summary>
        /// Create auth request for service key with <paramref name="keyIndex"/>, which can be used for authentication using server <paramref name="nonce"/>
        /// </summary>
        /// <param name="nonce">Server nonce</param>
        /// <param name="keyIndex">Service key index</param>
        /// <returns>Auth request</returns>
        Task<Auth> CreateAuthRequestAsync(
            AuthNonce nonce,
            uint keyIndex = 0);
    }
}