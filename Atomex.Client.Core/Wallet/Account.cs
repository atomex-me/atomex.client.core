using System;
using System.Collections.Generic;
using System.IO;
using System.Security;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Configuration;
using Serilog;

using Atomex.Abstract;
using Atomex.Blockchain;
using Atomex.Blockchain.Abstract;
using Atomex.Common;
using Atomex.Core;
using Atomex.Cryptography.Abstract;
using Atomex.LiteDb;
using Atomex.Wallet.Abstract;
using Atomex.Wallet.Tezos;

namespace Atomex.Wallet
{
    public class Account : IAccount
    {
        public const string DefaultUserSettingsFileName = "user.config";
        public const string DefaultDataFileName = "data.db";
        private const string DefaultAccountKey = "Account:Default";
        public string SettingsFilePath => $"{Path.GetDirectoryName(Wallet.PathToWallet)}/{DefaultUserSettingsFileName}";

        public event EventHandler<CurrencyEventArgs> BalanceUpdated
        {
            add
            {
                foreach (var currencyAccount in CurrencyAccounts)
                    currencyAccount.Value.BalanceUpdated += value;
            }
            remove
            {
                foreach (var currencyAccount in CurrencyAccounts)
                    currencyAccount.Value.BalanceUpdated -= value;
            }
        }
        public event EventHandler<TransactionEventArgs> UnconfirmedTransactionAdded
        {
            add
            {
                foreach (var currencyAccount in CurrencyAccounts.Values)
                    if (currencyAccount is ITransactionalAccount account)
                        account.UnconfirmedTransactionAdded += value;
            }
            remove
            {
                foreach (var currencyAccount in CurrencyAccounts.Values)
                    if (currencyAccount is ITransactionalAccount account)
                        account.UnconfirmedTransactionAdded -= value;
            }
        }
        public event EventHandler Locked;
        public event EventHandler Unlocked;

        public bool IsLocked => Wallet.IsLocked;
        public Network Network => Wallet.Network;
        public IHdWallet Wallet { get; }
        public ICurrencies Currencies { get; }
        public UserData UserData { get; private set; }
        private IAccountDataRepository DataRepository { get; }
        private IDictionary<string, ICurrencyAccount> CurrencyAccounts { get; }

        private Account(
            string pathToAccount,
            SecureString password,
            ICurrenciesProvider currenciesProvider,
            Action<MigrationActionType> migrationCompleteCallback = null)
            : this(wallet: HdWallet.LoadFromFile(pathToAccount, password),
                   password: password,
                   currenciesProvider: currenciesProvider,
                   migrationCompleteCallback: migrationCompleteCallback)
        {
        }

        public Account(
            IHdWallet wallet,
            SecureString password,
            ICurrenciesProvider currenciesProvider,
            Action<MigrationActionType> migrationCompleteCallback = null)
        {
            Wallet = wallet ?? throw new ArgumentNullException(nameof(wallet));

            Currencies = currenciesProvider.GetCurrencies(Network);

            DataRepository = new LiteDbAccountDataRepository(
                pathToDb: Path.Combine(Path.GetDirectoryName(Wallet.PathToWallet), DefaultDataFileName),
                password: password,
                currencies: Currencies,
                network: wallet.Network,
                migrationCompleteCallback);

            CurrencyAccounts = CurrencyAccountCreator.Create(Currencies, wallet, DataRepository);

            UserData = UserData.TryLoadFromFile(
                pathToFile: SettingsFilePath) ?? UserData.GetDefaultSettings(Currencies);
        }

        public Account(
            IHdWallet wallet,
            IAccountDataRepository dataRepository,
            ICurrenciesProvider currenciesProvider)
        {
            Wallet = wallet ?? throw new ArgumentNullException(nameof(wallet));
            DataRepository = dataRepository ?? throw new ArgumentNullException(nameof(dataRepository));

            Currencies = currenciesProvider.GetCurrencies(Network);
            CurrencyAccounts = CurrencyAccountCreator.Create(Currencies, wallet, DataRepository);

            UserData = UserData.TryLoadFromFile(
                pathToFile: SettingsFilePath) ?? UserData.GetDefaultSettings(Currencies);
        }

        #region Common

        public bool ChangePassword(SecureString newPassword)
        {
            var hdWallet = Wallet as HdWallet;

            hdWallet.KeyStorage.Encrypt(newPassword);

            if (!hdWallet.SaveToFile(Wallet.PathToWallet, newPassword))
                return false;

            UserData.SaveToFile(SettingsFilePath);
            DataRepository.ChangePassword(newPassword);

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

        public static IAccount LoadFromConfiguration(
            IConfiguration configuration,
            SecureString password,
            ICurrenciesProvider currenciesProvider)
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

            return LoadFromFile(pathToAccount, password, currenciesProvider);
        }

        public static Account LoadFromFile(
            string pathToAccount,
            SecureString password,
            ICurrenciesProvider currenciesProvider,
            Action<MigrationActionType> migrationCompleteCallback = null)
        {
            return new Account(pathToAccount, password, currenciesProvider, migrationCompleteCallback);
        }

        public ICurrencyAccount GetCurrencyAccount(string currency)
        {
            if (CurrencyAccounts.TryGetValue(currency, out var account))
                return account;

            throw new NotSupportedException($"Not supported currency {currency}");
        }

        public ICurrencyAccount GetTezosTokenAccount(
            string currency,
            string tokenContract,
            int tokenId)
        {
            var uniqueId = $"{currency}:{tokenContract}:{tokenId}";

            if (CurrencyAccounts.TryGetValue(uniqueId, out var account))
                return account;

            return CurrencyAccountCreator.CreateTezosTokenAccount(
                currency,
                tokenContract,
                tokenId,
                Currencies,
                Wallet,
                DataRepository,
                CurrencyAccounts[TezosConfig.Xtz] as TezosAccount);
        }

        public T GetCurrencyAccount<T>(string currency) where T : class, ICurrencyAccount =>
            GetCurrencyAccount(currency) as T;

        public T GetTezosTokenAccount<T>(
            string currency,
            string tokenContract,
            int tokenId) where T : class =>
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

        public Task<bool> RemoveAllOrdersAsync() =>
            DataRepository.RemoveAllOrdersAsync();

        public Order GetOrderById(string clientOrderId) =>
            DataRepository.GetOrderById(clientOrderId);

        public Order GetOrderById(long id) =>
            DataRepository.GetOrderById(id);

        public Task<bool> RemoveOrderByIdAsync(long id) =>
            DataRepository.RemoveOrderByIdAsync(id);

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