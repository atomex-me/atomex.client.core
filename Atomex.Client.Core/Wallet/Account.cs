using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Security;
using System.Threading;
using System.Threading.Tasks;

using Serilog;

using Atomex.Abstract;
using Atomex.Blockchain.Abstract;
using Atomex.Common;
using Atomex.Core;
using Atomex.Cryptography.Abstract;
using Atomex.Wallet.Abstract;
using Atomex.Wallet.Tezos;

namespace Atomex.Wallet
{
    public class Account : IAccount
    {
        public const string DefaultUserSettingsFileName = "user.config";
        public const string DefaultDataFileName = "data.db";
        public string SettingsFilePath => $"{Path.GetDirectoryName(Wallet.PathToWallet)}/{DefaultUserSettingsFileName}";

        public event EventHandler Locked;
        public event EventHandler Unlocked;

        public bool IsLocked => Wallet.IsLocked;
        public Network Network => Wallet.Network;
        public IHdWallet Wallet { get; }
        public ICurrencies Currencies { get; }
        public UserData UserData { get; private set; }

        private readonly ILocalStorage _localStorage;
        private readonly IDictionary<string, ICurrencyAccount> _currencyAccounts;

        public Account(
            IHdWallet wallet,
            ILocalStorage localStorage,
            ICurrenciesProvider currenciesProvider)
        {
            Wallet = wallet ?? throw new ArgumentNullException(nameof(wallet));
            _localStorage = localStorage ?? throw new ArgumentNullException(nameof(localStorage));

            Currencies = currenciesProvider.GetCurrencies(Network);
            _currencyAccounts = CurrencyAccountCreator.Create(Currencies, wallet, _localStorage);

            UserData = UserData.TryLoadFromFile(SettingsFilePath) ?? UserData.GetDefaultSettings(Currencies);
        }

        #region Common

        public bool ChangePassword(SecureString newPassword)
        {
            var hdWallet = Wallet as HdWallet;

            hdWallet.KeyStorage.Encrypt(newPassword);

            if (!hdWallet.SaveToFile(Wallet.PathToWallet, newPassword))
                return false;

            UserData.SaveToFile(SettingsFilePath);
            _localStorage.ChangePassword(newPassword);

            return true;
        }

        public void Lock()
        {
            Wallet.Lock();

            Locked?.Invoke(this, EventArgs.Empty);
        }

        public void Unlock(SecureString password)
        {
            Wallet.Unlock(password);

            Log.Information("Wallet is unlocked");

            Unlocked?.Invoke(this, EventArgs.Empty);
        }

        public IAccount UseUserSettings(UserData userData)
        {
            UserData = userData;
            return this;
        }

        public ICurrencyAccount GetCurrencyAccount(string currency)
        {
            if (_currencyAccounts.TryGetValue(currency, out var account))
                return account;

            throw new NotSupportedException($"Not supported currency {currency}");
        }

        public ICurrencyAccount GetTezosTokenAccount(
            string currency,
            string tokenContract,
            BigInteger tokenId)
        {
            var uniqueId = $"{currency}:{tokenContract}:{tokenId}";

            if (_currencyAccounts.TryGetValue(uniqueId, out var account))
                return account;

            return CurrencyAccountCreator.CreateTezosTokenAccount(
                tokenType: currency,
                tokenContract: tokenContract,
                tokenId: tokenId,
                currencies: Currencies,
                wallet: Wallet,
                localStorage: _localStorage,
                tezosAccount: _currencyAccounts[TezosConfig.Xtz] as TezosAccount);
        }

        public T GetCurrencyAccount<T>(string currency) where T : class, ICurrencyAccount =>
            GetCurrencyAccount(currency) as T;

        public T GetTezosTokenAccount<T>(
            string currency,
            string tokenContract,
            BigInteger tokenId) where T : class =>
            GetTezosTokenAccount(currency, tokenContract, tokenId) as T;

        public string GetUserId(uint keyIndex = 0)
        {
            using var servicePublicKey = Wallet.GetServicePublicKey(keyIndex);
            var publicKey = servicePublicKey.ToUnsecuredBytes();

            return HashAlgorithm.Sha256.Hash(publicKey, iterations: 2).ToHexString();
        }

        #endregion Common

        #region Balances

        public Task<Balance> GetBalanceAsync(
            string currency,
            CancellationToken cancellationToken = default)
        {
            return GetCurrencyAccount(currency)
                .GetBalanceAsync();
        }

        public Task<Balance> GetAddressBalanceAsync(
            string currency,
            string address,
            CancellationToken cancellationToken = default)
        {
            return GetCurrencyAccount(currency)
                .GetAddressBalanceAsync(address, cancellationToken);
        }

        public Task UpdateBalanceAsync(
            string currency,
            CancellationToken cancellationToken = default)
        {
            return GetCurrencyAccount(currency)
                .UpdateBalanceAsync(cancellationToken);
        }

        public Task UpdateBalanceAsync(
            string currency,
            string address,
            CancellationToken cancellationToken = default)
        {
            return GetCurrencyAccount(currency)
                .UpdateBalanceAsync(address, cancellationToken);
        }

        #endregion Balances

        #region Addresses

        public Task<WalletAddress> GetAddressAsync(
            string currency,
            string address,
            CancellationToken cancellationToken = default)
        {
            return GetCurrencyAccount(currency)
                .GetAddressAsync(address, cancellationToken);
        }

        public Task<IEnumerable<WalletAddress>> GetUnspentAddressesAsync(
            string currency,
            CancellationToken cancellationToken = default)
        {
            return GetCurrencyAccount(currency)
                .GetUnspentAddressesAsync(cancellationToken);
        }

        public Task<WalletAddress> GetFreeExternalAddressAsync(
            string currency,
            CancellationToken cancellationToken = default)
        {
            return GetCurrencyAccount(currency)
                .GetFreeExternalAddressAsync(cancellationToken);
        }

        #endregion Addresses

        #region Transactions

        public Task<T> GetTransactionByIdAsync<T>(
            string currency,
            string txId) where T : ITransaction
        {
            return _localStorage.GetTransactionByIdAsync<T>(
                currency: currency,
                txId: txId);
        }

        public Task<IEnumerable<T>> GetTransactionsAsync<T>(string currency)
            where T : ITransaction
        {
            return _localStorage.GetTransactionsAsync<T>(currency);
        }

        public Task<IEnumerable<ITransaction>> GetTransactionsAsync(string currency)
        {
            return _localStorage.GetTransactionsAsync(currency, Currencies.GetByName(currency).TransactionType);
        }

        public Task<IEnumerable<(ITransaction Tx, ITransactionMetadata Metadata)>> GetTransactionsWithMetadataAsync(
            string currency,
            int offset = 0,
            int limit = int.MaxValue,
            SortDirection sort = SortDirection.Desc,
            CancellationToken cancellationToken = default)
        {
            var config = Currencies.GetByName(currency);

            return _localStorage.GetTransactionsWithMetadataAsync(
                currency: currency,
                transactionType: config.TransactionType,
                metadataType: config.TransactionMetadataType,
                offset: offset,
                limit: limit,
                sort: sort,
                cancellationToken: cancellationToken);
        }

        public async Task<IEnumerable<ITransaction>> GetUnconfirmedTransactionsAsync()
        {
            var result = new List<ITransaction>();

            foreach (var (_, account) in _currencyAccounts)
            {
                var txs = await account
                    .GetUnconfirmedTransactionsAsync()
                    .ConfigureAwait(false);

                result.AddRange(txs);
            }

            return result;
        }

        public Task<ITransactionMetadata> ResolveTransactionMetadataAsync(
            ITransaction tx,
            CancellationToken cancellationToken = default)
        {
            return GetCurrencyAccount(tx.Currency)
                .ResolveTransactionMetadataAsync(tx, cancellationToken);
        }

        #endregion Transactions

        #region Orders

        public Task<bool> UpsertOrderAsync(Order order) =>
            _localStorage.UpsertOrderAsync(order);

        public Task<bool> RemoveAllOrdersAsync() =>
            _localStorage.RemoveAllOrdersAsync();

        public Order GetOrderById(string clientOrderId) =>
            _localStorage.GetOrderById(clientOrderId);

        public Order GetOrderById(long id) =>
            _localStorage.GetOrderById(id);

        public Task<bool> RemoveOrderByIdAsync(long id) =>
            _localStorage.RemoveOrderByIdAsync(id);

        #endregion Orders

        #region Swaps

        public Task<bool> AddSwapAsync(Swap swap) =>
            _localStorage.AddSwapAsync(swap);

        public Task<bool> UpdateSwapAsync(Swap swap) =>
            _localStorage.UpdateSwapAsync(swap);

        public Task<Swap> GetSwapByIdAsync(long swapId) =>
            _localStorage.GetSwapByIdAsync(swapId);

        public Task<IEnumerable<Swap>> GetSwapsAsync() =>
            _localStorage.GetSwapsAsync();

        #endregion Swaps
    }
}