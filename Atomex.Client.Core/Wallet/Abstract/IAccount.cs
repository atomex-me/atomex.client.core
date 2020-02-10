using System;
using System.Collections.Generic;
using System.Security;
using System.Threading;
using System.Threading.Tasks;
using Atomex.Abstract;
using Atomex.Blockchain;
using Atomex.Blockchain.Abstract;
using Atomex.Core;

namespace Atomex.Wallet.Abstract
{
    public interface IAccount : IAddressResolver
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
        IHdWallet Wallet { get; }

        /// <summary>
        /// Get all currencies supported by wallet
        /// </summary>
        ICurrencies Currencies { get; }

        /// <summary>
        /// Get all symbols supported by wallet
        /// </summary>
        ISymbols Symbols { get; }

        /// <summary>
        /// Get user settings
        /// </summary>
        UserSettings UserSettings { get; }

        #region Common

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
        IAccount UseUserSettings(UserSettings userSettings);

        ///// <summary>
        ///// Send <paramref name="amount"/> from <paramref name="from"/> with <paramref name="fee"/> and <paramref name="feePrice"/> to address <paramref name="to"/>
        ///// </summary>
        ///// <param name="currency">Currency</param>
        ///// <param name="from">From addresses</param>
        ///// <param name="to">Destination address</param>
        ///// <param name="amount">Amount to send</param>
        ///// <param name="fee">Fee</param>
        ///// <param name="feePrice">Fee price</param>
        ///// <param name="cancellationToken">Cancellation token</param>
        ///// <returns>Null if success, otherwise false</returns>
        //Task<Error> SendAsync(
        //    string currency,
        //    IEnumerable<WalletAddress> from,
        //    string to,
        //    decimal amount,
        //    decimal fee,
        //    decimal feePrice,
        //    CancellationToken cancellationToken = default);

        /// <summary>
        /// Send <paramref name="amount"/> from <paramref name="from"/> with <paramref name="fee"/> and <paramref name="feePrice"/> to address <paramref name="to"/>
        /// </summary>
        /// <param name="currency">Currency</param>
        /// <param name="from">From addresses</param>
        /// <param name="to">Destination address</param>
        /// <param name="amount">Amount to send</param>
        /// <param name="fee">Fee</param>
        /// <param name="feePrice">Fee price</param>
        /// <param name="useDefaultFee">Dafault fee mode</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Null if success, otherwise false</returns>
        Task<Error> SendAsync(
            string currency,
            IEnumerable<WalletAddress> from,
            string to,
            decimal amount,
            decimal fee,
            decimal feePrice,
            bool useDefaultFee = false,
            CancellationToken cancellationToken = default);

        ///// <summary>
        ///// Send <paramref name="amount"/> with <paramref name="fee"/> and <paramref name="feePrice"/> to address <paramref name="to"/>
        ///// </summary>
        ///// <param name="currency">Currency</param>
        ///// <param name="to">Destination address</param>
        ///// <param name="amount">Amount to send</param>
        ///// <param name="fee">Fee</param>
        ///// <param name="feePrice">Fee price</param>
        ///// <param name="cancellationToken">Cancellation token</param>
        ///// <returns>Null if success, otherwise false</returns>
        //Task<Error> SendAsync(
        //    string currency,
        //    string to,
        //    decimal amount,
        //    decimal fee,
        //    decimal feePrice,
        //    CancellationToken cancellationToken = default);

        /// <summary>
        /// Send <paramref name="amount"/> with <paramref name="fee"/> and <paramref name="feePrice"/> to address <paramref name="to"/>
        /// </summary>
        /// <param name="currency">Currency</param>
        /// <param name="to">Destination address</param>
        /// <param name="amount">Amount to send</param>
        /// <param name="fee">Fee</param>
        /// <param name="feePrice">Fee price</param>
        /// <param name="useDefaultFee">Dafault fee mode</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Null if success, otherwise false</returns>
        Task<Error> SendAsync(
            string currency,
            string to,
            decimal amount,
            decimal fee,
            decimal feePrice,
            bool useDefaultFee = false,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Estimate fee for transfer <paramref name="amount"/> to address <paramref name="to"/> for <paramref name="currency"/>
        /// </summary>
        /// <param name="currency">Currency</param>
        /// <param name="to">Destination address (can be null)</param>
        /// <param name="amount">Amount to send</param>
        /// <param name="type">Blockchain transaction type</param>
        /// <param name="inputFee">fee input in send dialog</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Estimated fees or null if insufficient funds</returns>
        Task<decimal?> EstimateFeeAsync(
            string currency,
            string to,
            decimal amount,
            BlockchainTransactionType type,
            decimal inputFee = 0,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Estimate max amount and fee to send
        /// </summary>
        /// <param name="currency">Currency</param>
        /// <param name="to">Destination address (can be null)</param>
        /// <param name="type">Blockchain transaction type</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Max amount and fee to send</returns>
        Task<(decimal, decimal, decimal)> EstimateMaxAmountToSendAsync(
            string currency,
            string to,
            BlockchainTransactionType type,
            bool reserve = false,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Create auth request for service key with <paramref name="keyIndex"/>, which can be used for authentication using server <paramref name="nonce"/>
        /// </summary>
        /// <param name="nonce">Server nonce</param>
        /// <param name="keyIndex">Service key index</param>
        /// <returns>Auth request</returns>
        Task<Auth> CreateAuthRequestAsync(AuthNonce nonce, uint keyIndex = 0);

        ICurrencyAccount GetCurrencyAccount(string currency);

        T GetCurrencyAccount<T>(string currency) where T : class, ICurrencyAccount;

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
        /// Gets address for <paramref name="currency"/>, <paramref name="chain"/> and key <paramref name="index"/>
        /// </summary>
        /// <param name="currency">Currency</param>
        /// <param name="chain">Chain</param>
        /// <param name="index">Key index</param>
        /// <returns>Address</returns>
        Task<WalletAddress> DivideAddressAsync(
            string currency,
            int chain,
            uint index);

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
        /// Gets unspent addresses for <paramref name="currency"/> and <paramref name="amount"/> with <paramref name="fee"/>
        /// </summary>
        /// <param name="currency">Currency</param>
        /// <param name="toAddress">Destination adderss</param>
        /// <param name="amount">Required amount</param>
        /// <param name="fee">Required fee</param>
        /// <param name="feePrice">Required fee price</param>
        /// <param name="feeUsagePolicy">Fee usage policy</param>
        /// <param name="addressUsagePolicy">Address usage policy</param>
        /// <param name="transactionType">Transaction type</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Set of unspent addresses</returns>
        Task<IEnumerable<WalletAddress>> GetUnspentAddressesAsync(
            string currency,
            string toAddress,
            decimal amount,
            decimal fee,
            decimal feePrice,
            FeeUsagePolicy feeUsagePolicy,
            AddressUsagePolicy addressUsagePolicy,
            BlockchainTransactionType transactionType,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets free internal address for <paramref name="currency"/>
        /// </summary>
        /// <param name="currency">Currency</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Wallet address</returns>
        Task<WalletAddress> GetFreeInternalAddressAsync(
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

        /// <summary>
        /// Get refund address for <paramref name="currency"/>
        /// </summary>
        /// <param name="currency">Currency</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Wallet address</returns>
        //Task<WalletAddress> GetRefundAddressAsync(
        //    string currency,
        //    CancellationToken cancellationToken = default);

        /// <summary>
        /// Get redeem address for <paramref name="currency"/>
        /// </summary>
        /// <param name="currency">Currency</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Wallet address</returns>
        Task<WalletAddress> GetRedeemAddressAsync(
            string currency,
            CancellationToken cancellationToken = default);

        #endregion Addresses

        #region Transactions

        Task UpsertTransactionAsync(
            IBlockchainTransaction tx,
            bool updateBalance = false,
            bool notifyIfUnconfirmed = true,
            bool notifyIfBalanceUpdated = true,
            CancellationToken cancellationToken = default);

        Task<IBlockchainTransaction> GetTransactionByIdAsync(string currency, string txId);
        Task<IEnumerable<IBlockchainTransaction>> GetTransactionsAsync(string currency);
        Task<IEnumerable<IBlockchainTransaction>> GetTransactionsAsync();
        Task<bool> RemoveTransactionAsync(string id);

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