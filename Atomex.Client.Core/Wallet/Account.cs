#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Security;
using System.Threading;
using System.Threading.Tasks;

using Serilog;

using Atomex.Abstract;
using Atomex.Blockchain.Abstract;
using Atomex.Blockchain.Ethereum;
using Atomex.Blockchain.Tezos;
using Atomex.Common;
using Atomex.Core;
using Atomex.Cryptography.Abstract;
using Atomex.Wallet.Abstract;
using Atomex.Wallets;
using Atomex.Wallets.Abstract;
using ILogger = Microsoft.Extensions.Logging.ILogger;

namespace Atomex.Wallet
{
    public class Account : IAccount
    {
        public const string DefaultUserSettingsFileName = "user.config";
        public const string DefaultDataFileName = "data.db";
        public string SettingsFilePath => $"{Path.GetDirectoryName(Wallet.PathToWallet)}/{DefaultUserSettingsFileName}";

        public event EventHandler? Locked;
        public event EventHandler? Unlocked;

        public bool IsLocked => Wallet.IsLocked;
        public Network Network => Wallet.Network;
        public HdWallet Wallet { get; }
        public ICurrencies Currencies { get; }
        public UserData UserData { get; private set; }

        private readonly ILocalStorage _localStorage;
        private readonly IDictionary<string, ICurrencyAccount> _accountsCache;

        public Account(
            HdWallet wallet,
            ILocalStorage localStorage,
            ICurrenciesProvider currenciesProvider)
        {
            Wallet = wallet ?? throw new ArgumentNullException(nameof(wallet));
            _localStorage = localStorage ?? throw new ArgumentNullException(nameof(localStorage));

            Currencies = currenciesProvider.GetCurrencies(Network);   
            _accountsCache = new ConcurrentDictionary<string, ICurrencyAccount>();

            UserData = UserData.TryLoadFromFile(SettingsFilePath) ?? UserData.GetDefaultSettings(Currencies);
        }

        #region Common

        public bool ChangePassword(SecureString newPassword)
        {
            Wallet.KeyStorage.Encrypt(newPassword);

            if (!Wallet.SaveToFile(Wallet.PathToWallet, newPassword))
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

        public ICurrencyAccount GetCurrencyAccount(
            string currency,
            string? tokenContract = null,
            BigInteger? tokenId = null)
        {
            if (Atomex.Currencies.IsTokenStandard(currency) && tokenContract == null)
                throw new ArgumentException($"Token contract cannot be null for token {currency} standard");

            if (Atomex.Currencies.IsPresetToken(currency))
            {
                if (Currencies.GetByName(currency) is not ITokenConfig tokenConfig)
                    throw new ArgumentException($"Can't find config for {currency}");

                if (tokenConfig.TokenContractAddress == null)
                    throw new Exception($"Token contract address for {currency} is null");

                currency = tokenConfig.Standard;

                if (tokenContract != null && tokenContract != tokenConfig.TokenContractAddress)
                    throw new ArgumentException($"The token contract {tokenContract} does not match the token contract {tokenConfig.TokenContractAddress} in the configuration");

                tokenContract = tokenConfig.TokenContractAddress;

                if (tokenId != null && tokenId != tokenConfig.TokenId)
                    throw new ArgumentException($"The token id {tokenId} does not match the token id {tokenConfig.TokenId} in the configuration");

                tokenId = tokenConfig.TokenId;
            }

            var uniqueId = tokenContract != null
                ? $"{currency}:{tokenContract}:{tokenId ?? 0}"
                : currency;

            if (_accountsCache.TryGetValue(uniqueId, out var account))
                return account;

            var baseChainAccount = Atomex.Currencies.IsEthereumTokenStandard(currency)
                ? GetCurrencyAccount(EthereumHelper.Eth)
                : Atomex.Currencies.IsTezosTokenStandard(currency)
                    ? GetCurrencyAccount(TezosHelper.Xtz)
                    : null;

            account = CurrencyAccountCreator.CreateCurrencyAccount(
                currency: currency,
                wallet: Wallet,
                localStorage: _localStorage,
                currencies: Currencies,
                tokenContract: tokenContract,
                tokenId: tokenId,
                baseChainAccount: baseChainAccount);

            if (account == null)
                throw new NotSupportedException($"Can't create account for currency {currency}");

            _accountsCache.TryAdd(uniqueId, account);

            return account;
        }

        public T GetCurrencyAccount<T>(
            string currency,
            string? tokenContract = null,
            BigInteger? tokenId = null) where T : class, ICurrencyAccount =>
            GetCurrencyAccount(currency, tokenContract, tokenId) as T;

        public string GetUserId(uint keyIndex = 0)
        {
            var publicKey = Wallet.GetServicePublicKey(keyIndex);

            return HashAlgorithm.Sha256.Hash(publicKey, iterations: 2).ToHexString();
        }

        #endregion Common

        #region Balances

        public Task<Balance> GetBalanceAsync(
            string currency,
            string? tokenContract = null,
            BigInteger? tokenId = null,
            CancellationToken cancellationToken = default)
        {
            return GetCurrencyAccount(currency, tokenContract, tokenId)
                .GetBalanceAsync();
        }

        public Task<Balance> GetAddressBalanceAsync(
            string currency,
            string address,
            string? tokenContract = null,
            BigInteger? tokenId = null,
            CancellationToken cancellationToken = default)
        {
            return GetCurrencyAccount(currency, tokenContract, tokenId)
                .GetAddressBalanceAsync(address, cancellationToken);
        }

        public Task UpdateBalanceAsync(
            string currency,
            string? tokenContract = null,
            BigInteger? tokenId = null,
            ILogger? logger = null,
            CancellationToken cancellationToken = default)
        {
            return GetCurrencyAccount(currency, tokenContract, tokenId)
                .UpdateBalanceAsync(logger, cancellationToken);
        }

        public Task UpdateBalanceAsync(
            string currency,
            string address,
            string? tokenContract = null,
            BigInteger? tokenId = null,
            ILogger? logger = null,
            CancellationToken cancellationToken = default)
        {
            return GetCurrencyAccount(currency, tokenContract, tokenId)
                .UpdateBalanceAsync(address, logger, cancellationToken);
        }

        #endregion Balances

        #region Addresses

        public Task<WalletAddress> GetAddressAsync(
            string currency,
            string address,
            string? tokenContract = null,
            BigInteger? tokenId = null,
            CancellationToken cancellationToken = default)
        {
            return GetCurrencyAccount(currency, tokenContract, tokenId)
                .GetAddressAsync(address, cancellationToken);
        }

        public Task<IEnumerable<WalletAddress>> GetUnspentAddressesAsync(
            string currency,
            string? tokenContract = null,
            BigInteger? tokenId = null,
            CancellationToken cancellationToken = default)
        {
            return GetCurrencyAccount(currency, tokenContract, tokenId)
                .GetUnspentAddressesAsync(cancellationToken);
        }

        public Task<WalletAddress> GetFreeExternalAddressAsync(
            string currency,
            string? tokenContract = null,
            BigInteger? tokenId = null,
            CancellationToken cancellationToken = default)
        {
            return GetCurrencyAccount(currency, tokenContract, tokenId)
                .GetFreeExternalAddressAsync(cancellationToken);
        }

        #endregion Addresses

        #region Transactions

        public Task<IEnumerable<TransactionInfo<ITransaction, ITransactionMetadata>>> GetTransactionsWithMetadataAsync(
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

            foreach (var (_, account) in _accountsCache)
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
            if (Atomex.Currencies.IsTokenStandard(tx.Currency))
            {
                var tokenTransfer = (ITokenTransfer)tx;

                return GetCurrencyAccount(tokenTransfer.Currency, tokenTransfer.Contract, tokenTransfer.TokenId)
                    .ResolveTransactionMetadataAsync(tx, cancellationToken);
            }

            return GetCurrencyAccount(tx.Currency)
                .ResolveTransactionMetadataAsync(tx, cancellationToken);
        }

        #endregion Transactions

        #region Orders

        public Order GetOrderById(long id) =>
            _localStorage.GetOrderById(id);

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