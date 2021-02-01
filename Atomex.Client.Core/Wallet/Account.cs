﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Configuration;
using Serilog;

using Atomex.Abstract;
using Atomex.Api;
using Atomex.Blockchain;
using Atomex.Blockchain.Abstract;
using Atomex.Common;
using Atomex.Core;
using Atomex.LiteDb;
using Atomex.Wallet.Abstract;

namespace Atomex.Wallet
{
    public class Account : IAccount
    {
        public const string DefaultUserSettingsFileName = "user.config";

        private const string DefaultDataFileName = "data.db";
        private const string DefaultAccountKey = "Account:Default";
        private const string ApiVersion = "1.5";

        public event EventHandler<CurrencyEventArgs> BalanceUpdated
        {
            add {
                foreach (var currencyAccount in CurrencyAccounts)
                    currencyAccount.Value.BalanceUpdated += value;
            }
            remove {
                foreach (var currencyAccount in CurrencyAccounts)
                    currencyAccount.Value.BalanceUpdated -= value;
            }
        }
        public event EventHandler<TransactionEventArgs> UnconfirmedTransactionAdded
        {
            add {
                foreach (var currencyAccount in CurrencyAccounts)
                    currencyAccount.Value.UnconfirmedTransactionAdded += value;
            }
            remove {
                foreach (var currencyAccount in CurrencyAccounts)
                    currencyAccount.Value.UnconfirmedTransactionAdded -= value;
            }
        }
        public event EventHandler Locked;
        public event EventHandler Unlocked;

        public bool IsLocked => Wallet.IsLocked;
        public Network Network => Wallet.Network;
        public IHdWallet Wallet { get; }
        public ICurrencies Currencies { get; }
        public UserSettings UserSettings { get; private set; }

        private ClientType _clientType;
        private IAccountDataRepository DataRepository { get; }
        private IDictionary<string, ICurrencyAccount> CurrencyAccounts { get; }


        private Account(
            string pathToAccount,
            SecureString password,
            ICurrenciesProvider currenciesProvider,
            ISymbolsProvider symbolsProvider,
            ClientType clientType)
            : this(wallet: HdWallet.LoadFromFile(pathToAccount, password),
                   password: password,
                   currenciesProvider: currenciesProvider,
                   symbolsProvider : symbolsProvider,
                   clientType: clientType)
        {
        }

        public Account(
            IHdWallet wallet,
            SecureString password,
            ICurrenciesProvider currenciesProvider,
            ISymbolsProvider symbolsProvider,
            ClientType clientType)
        {
            Wallet = wallet ?? throw new ArgumentNullException(nameof(wallet));

            Currencies = currenciesProvider.GetCurrencies(Network);

            DataRepository = new LiteDbAccountDataRepository(
                pathToDb: Path.Combine(Path.GetDirectoryName(Wallet.PathToWallet), DefaultDataFileName),
                password: password,
                currencies: Currencies,
                network: wallet.Network);

            CurrencyAccounts = Currencies
                .ToDictionary(
                    c => c.Name,
                    c => CurrencyAccountCreator.Create(
                        currency: c.Name,
                        wallet: Wallet,
                        dataRepository: DataRepository,
                        currencies: Currencies));

            UserSettings = UserSettings.TryLoadFromFile(
                pathToFile: $"{Path.GetDirectoryName(Wallet.PathToWallet)}/{DefaultUserSettingsFileName}",
                password: password) ?? UserSettings.DefaultSettings;

            _clientType = clientType;
        }

        public Account(
            IHdWallet wallet,
            SecureString password,
            IAccountDataRepository dataRepository,
            ICurrenciesProvider currenciesProvider,
            ISymbolsProvider symbolsProvider,
            ClientType clientType)
        {
            Wallet = wallet ?? throw new ArgumentNullException(nameof(wallet));
            DataRepository = dataRepository ?? throw new ArgumentNullException(nameof(dataRepository));

            Currencies = currenciesProvider.GetCurrencies(Network);

            CurrencyAccounts = Currencies
                .ToDictionary(
                    c => c.Name,
                    c => CurrencyAccountCreator.Create(
                        currency: c.Name,
                        wallet: Wallet,
                        dataRepository: DataRepository,
                        currencies: Currencies));

            UserSettings = UserSettings.TryLoadFromFile(
                pathToFile: $"{Path.GetDirectoryName(Wallet.PathToWallet)}/{DefaultUserSettingsFileName}",
                password: password) ?? UserSettings.DefaultSettings;

            _clientType = clientType;
        }

        #region Common

        public void Lock()
        {
            Wallet.Lock();

            Locked?.Invoke(this, EventArgs.Empty);
        }

        public void Unlock(SecureString password)
        {
            Wallet.Unlock(password);

            Unlocked?.Invoke(this, EventArgs.Empty);
        }

        public IAccount UseUserSettings(UserSettings userSettings)
        {
            UserSettings = userSettings;
            return this;
        }

        public Task<Error> SendAsync(
            string currency,
            IEnumerable<WalletAddress> from,
            string to,
            decimal amount,
            decimal fee,
            decimal feePrice,
            bool useDefaultFee = false,
            CancellationToken cancellationToken = default)
        {
            return GetCurrencyAccount(currency)
                .SendAsync(
                    from: from,
                    to: to,
                    amount: amount,
                    fee: fee,
                    feePrice: feePrice,
                    useDefaultFee: useDefaultFee,
                    cancellationToken: cancellationToken);
        }

        public Task<Error> SendAsync(
            string currency,
            string to,
            decimal amount,
            decimal fee,
            decimal feePrice,
            bool useDefaultFee = false,
            CancellationToken cancellationToken = default)
        {
            return GetCurrencyAccount(currency)
                .SendAsync(
                    to: to,
                    amount: amount,
                    fee: fee,
                    feePrice: feePrice,
                    useDefaultFee: useDefaultFee,
                    cancellationToken: cancellationToken);
        }

        public Task<decimal?> EstimateFeeAsync(
            string currency,
            string to,
            decimal amount,
            BlockchainTransactionType type,
            decimal fee = 0,
            decimal feePrice = 0,
            CancellationToken cancellationToken = default)
        {
            return GetCurrencyAccount(currency)
                .EstimateFeeAsync(to, amount, type, fee, feePrice, cancellationToken);
        }

        public Task<(decimal, decimal, decimal)> EstimateMaxAmountToSendAsync(
            string currency,
            string to,
            BlockchainTransactionType type,
            decimal fee = 0,
            decimal feePrice = 0, 
            bool reserve = false,
            CancellationToken cancellationToken = default)
        {
            return GetCurrencyAccount(currency)
                .EstimateMaxAmountToSendAsync(to, type, fee, feePrice, reserve, cancellationToken);
        }

        public Task<decimal> EstimateMaxFeeAsync(
            string currency,
            string to,
            decimal amount,
            BlockchainTransactionType type,
            CancellationToken cancellationToken = default)
        {
            return GetCurrencyAccount(currency)
                .EstimateMaxFeeAsync(to, amount, type, cancellationToken);
        }

        public async Task<Auth> CreateAuthRequestAsync(AuthNonce nonce, uint keyIndex = 0)
        {
            if (IsLocked)
            {
                Log.Warning("Wallet locked");
                return null;
            }

            using var securePublicKey = Wallet.GetServicePublicKey(keyIndex);
            using var publicKey = securePublicKey.ToUnsecuredBytes();

            var auth = new Auth
            {
                TimeStamp    = DateTime.UtcNow,
                Nonce        = nonce.Nonce,
                ClientNonce  = Guid.NewGuid().ToString(),
                PublicKeyHex = publicKey.Data.ToHexString(),
                Version      = $"{ApiVersion} {_clientType}"
            };

            var signature = await Wallet
                .SignByServiceKeyAsync(auth.SignedData, keyIndex)
                .ConfigureAwait(false);

            auth.Signature = Convert.ToBase64String(signature);

            return auth;
        }

        public static IAccount LoadFromConfiguration(
            IConfiguration configuration,
            SecureString password,
            ICurrenciesProvider currenciesProvider,
            ISymbolsProvider symbolsProvider,
            ClientType clientType)
        {
            var pathToAccount = configuration[DefaultAccountKey];

            if (string.IsNullOrEmpty(pathToAccount))
            {
                Log.Error("Path to default account is null or empty");
                return null;
            }

            if (!File.Exists(FileSystem.Current.ToFullPath(pathToAccount)))
            {
                Log.Error("Default account not found");
                return null;
            }

            return LoadFromFile(pathToAccount, password, currenciesProvider, symbolsProvider, clientType);
        }

        public static Account LoadFromFile(
            string pathToAccount,
            SecureString password,
            ICurrenciesProvider currenciesProvider,
            ISymbolsProvider symbolsProvider,
            ClientType clientType)
        {
            return new Account(pathToAccount, password, currenciesProvider, symbolsProvider, clientType);
        }

        public ICurrencyAccount GetCurrencyAccount(string currency)
        {
            if (CurrencyAccounts.TryGetValue(currency, out var account))
                return account;

            throw new NotSupportedException($"Not supported currency {currency}");
        }

        public T GetCurrencyAccount<T>(string currency) where T : class, ICurrencyAccount =>
            GetCurrencyAccount(currency) as T;

        #endregion Common

        #region Balances

        public Task<Balance> GetBalanceAsync(
            string currency,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(GetCurrencyAccount(currency).GetBalance());
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

        public Task<WalletAddress> DivideAddressAsync(
            string currency,
            int chain,
            uint index)
        {
            return GetCurrencyAccount(currency)
                .DivideAddressAsync(chain, index);
        }

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

        public Task<IEnumerable<WalletAddress>> GetUnspentTokenAddressesAsync(
            string currency,
            CancellationToken cancellationToken = default)
        {
            return GetCurrencyAccount(currency)
                .GetUnspentTokenAddressesAsync(cancellationToken);
        }

        public Task<IEnumerable<WalletAddress>> GetUnspentAddressesAsync(
            string currency,
            string toAddress,
            decimal amount,
            decimal fee,
            decimal feePrice,
            FeeUsagePolicy feeUsagePolicy,
            AddressUsagePolicy addressUsagePolicy,
            BlockchainTransactionType transactionType,
            CancellationToken cancellationToken = default)
        {
            return GetCurrencyAccount(currency)
                .GetUnspentAddressesAsync(
                    toAddress: toAddress,
                    amount: amount,
                    fee: fee,
                    feePrice: feePrice,
                    feeUsagePolicy: feeUsagePolicy,
                    addressUsagePolicy: addressUsagePolicy,
                    transactionType: transactionType,
                    cancellationToken: cancellationToken);
        }

        public Task<WalletAddress> GetFreeInternalAddressAsync(
            string currency,
            CancellationToken cancellationToken = default)
        {
            return GetCurrencyAccount(currency)
                .GetFreeInternalAddressAsync(cancellationToken);
        }

        public Task<WalletAddress> GetFreeExternalAddressAsync(
            string currency,
            CancellationToken cancellationToken = default)
        {
            return GetCurrencyAccount(currency)
                .GetFreeExternalAddressAsync(cancellationToken);
        }

        public Task<WalletAddress> GetRedeemAddressAsync(   //todo: check if always returns the biggest address
            string currency,
            CancellationToken cancellationToken = default)
        {
            return GetCurrencyAccount(currency)
                .GetRedeemAddressAsync(cancellationToken);   
        }

        #endregion Addresses

        #region Transactions

        public Task UpsertTransactionAsync(
            IBlockchainTransaction tx,
            bool updateBalance = false,
            bool notifyIfUnconfirmed = true,
            bool notifyIfBalanceUpdated = true,
            CancellationToken cancellationToken = default)
        {
            return GetCurrencyAccount(tx.Currency.Name)
                .UpsertTransactionAsync(
                    tx: tx,
                    updateBalance: updateBalance,
                    notifyIfUnconfirmed: notifyIfUnconfirmed,
                    notifyIfBalanceUpdated: notifyIfBalanceUpdated,
                    cancellationToken: cancellationToken);
        }

        public Task<IBlockchainTransaction> GetTransactionByIdAsync(
            string currency,
            string txId)
        {
            return DataRepository.GetTransactionByIdAsync(
                currency: currency,
                txId: txId,
                transactionType: Currencies.GetByName(currency).TransactionType);
        }

        public Task<IEnumerable<IBlockchainTransaction>> GetTransactionsAsync(string currency)
        {
            return DataRepository.GetTransactionsAsync(
                currency: currency,
                transactionType: Currencies.GetByName(currency).TransactionType);
        }

        public async Task<IEnumerable<IBlockchainTransaction>> GetTransactionsAsync()
        {
            var result = new List<IBlockchainTransaction>();

            foreach (var currency in Currencies)
            {
                var txs = await GetTransactionsAsync(currency.Name)
                    .ConfigureAwait(false);

                result.AddRange(txs);
            }

            return result;
        }

        public Task<bool> RemoveTransactionAsync(string id) =>
            DataRepository.RemoveTransactionByIdAsync(id);

        #endregion Transactions

        #region Orders

        public Task<bool> UpsertOrderAsync(Order order) =>
            DataRepository.UpsertOrderAsync(order);

        public Order GetOrderById(string clientOrderId) =>
            DataRepository.GetOrderById(clientOrderId);

        public Order GetOrderById(long id) =>
            DataRepository.GetOrderById(id);

        #endregion Orders

        #region Swaps

        public Task<bool> AddSwapAsync(Swap swap) =>
            DataRepository.AddSwapAsync(swap);

        public Task<bool> UpdateSwapAsync(Swap swap) =>
            DataRepository.UpdateSwapAsync(swap);

        public Task<Swap> GetSwapByIdAsync(long swapId) =>
            DataRepository.GetSwapByIdAsync(swapId);

        public Task<IEnumerable<Swap>> GetSwapsAsync() =>
            DataRepository.GetSwapsAsync();

        #endregion Swaps
    }
}