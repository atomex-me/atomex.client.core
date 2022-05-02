using System;
using System.Collections.Generic;
using System.IO;
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
using Atomex.Cryptography.Abstract;
using Atomex.LiteDb;
using Atomex.Wallet.Abstract;
using Atomex.Wallet.Tezos;

namespace Atomex.Wallet
{
    public class Account_OLD : IAccount_OLD
    {
        public const string DefaultUserSettingsFileName = "user.config";

        private const string DefaultDataFileName = "data.db";
        private const string DefaultAccountKey = "Account:Default";
        private const string ApiVersion = "1.5";
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
        public IHdWallet_OLD Wallet { get; }
        public ICurrencies Currencies { get; }
        public UserSettings UserSettings { get; private set; }

        private readonly ClientType _clientType;
        private IAccountDataRepository_OLD DataRepository { get; }
        private IDictionary<string, ICurrencyAccount_OLD> CurrencyAccounts { get; }

        private Account_OLD(
            string pathToAccount,
            SecureString password,
            ICurrenciesProvider currenciesProvider,
            ClientType clientType,
            Action<MigrationActionType> migrationCompleteCallback = null)
            : this(wallet: HdWallet_OLD.LoadFromFile(pathToAccount, password),
                   password: password,
                   currenciesProvider: currenciesProvider,
                   clientType: clientType,
                   migrationCompleteCallback: migrationCompleteCallback)
        {
        }

        public Account_OLD(
            IHdWallet_OLD wallet,
            SecureString password,
            ICurrenciesProvider currenciesProvider,
            ClientType clientType,
            Action<MigrationActionType> migrationCompleteCallback = null)
        {
            Wallet = wallet ?? throw new ArgumentNullException(nameof(wallet));

            Currencies = currenciesProvider.GetCurrencies(Network);

            DataRepository = new LiteDbAccountDataRepository_OLD(
                pathToDb: Path.Combine(Path.GetDirectoryName(Wallet.PathToWallet), DefaultDataFileName),
                password: password,
                currencies: Currencies,
                network: wallet.Network,
                migrationCompleteCallback);

            CurrencyAccounts = CurrencyAccountCreator.Create(Currencies, wallet, DataRepository);

            UserSettings = UserSettings.TryLoadFromFile(
                pathToFile: SettingsFilePath) ?? UserSettings.GetDefaultSettings(Currencies);

            _clientType = clientType;
        }

        public Account_OLD(
            IHdWallet_OLD wallet,
            IAccountDataRepository_OLD dataRepository,
            ICurrenciesProvider currenciesProvider,
            ClientType clientType)
        {
            Wallet = wallet ?? throw new ArgumentNullException(nameof(wallet));
            DataRepository = dataRepository ?? throw new ArgumentNullException(nameof(dataRepository));

            Currencies = currenciesProvider.GetCurrencies(Network);
            CurrencyAccounts = CurrencyAccountCreator.Create(Currencies, wallet, DataRepository);

            UserSettings = UserSettings.TryLoadFromFile(
                pathToFile: SettingsFilePath) ?? UserSettings.GetDefaultSettings(Currencies);

            _clientType = clientType;
        }

        #region Common

        public void ChangePassword(SecureString newPassword)
        {
            var hdWallet = Wallet as HdWallet_OLD;

            hdWallet.KeyStorage.Encrypt(newPassword);
            hdWallet.SaveToFile(Wallet.PathToWallet, newPassword);

            UserSettings.SaveToFile(SettingsFilePath);
            DataRepository.ChangePassword(newPassword);
        }

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

        public IAccount_OLD UseUserSettings(UserSettings userSettings)
        {
            UserSettings = userSettings;
            return this;
        }

        public async Task<Auth> CreateAuthRequestAsync(AuthNonce nonce, uint keyIndex = 0)
        {
            if (IsLocked)
            {
                Log.Warning("Wallet locked");
                return null;
            }

            using var securePublicKey = Wallet.GetServicePublicKey(keyIndex);
            var publicKey = securePublicKey.ToUnsecuredBytes();

            var auth = new Auth
            {
                TimeStamp    = DateTime.UtcNow,
                Nonce        = nonce.Nonce,
                ClientNonce  = Guid.NewGuid().ToString(),
                PublicKeyHex = publicKey.ToHexString(),
                Version      = $"{ApiVersion} {_clientType}"
            };

            var hashToSign = HashAlgorithm.Sha256.Hash(auth.SignedData);

            var signature = await Wallet
                .SignByServiceKeyAsync(hashToSign, keyIndex)
                .ConfigureAwait(false);

            auth.Signature = Convert.ToBase64String(signature);

            return auth;
        }

        public static IAccount_OLD LoadFromConfiguration(
            IConfiguration configuration,
            SecureString password,
            ICurrenciesProvider currenciesProvider,
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

            return LoadFromFile(pathToAccount, password, currenciesProvider, clientType);
        }

        public static Account_OLD LoadFromFile(
            string pathToAccount,
            SecureString password,
            ICurrenciesProvider currenciesProvider,
            ClientType clientType,
            Action<MigrationActionType> migrationCompleteCallback = null)
        {
            return new Account_OLD(pathToAccount, password, currenciesProvider, clientType, migrationCompleteCallback);
        }

        public ICurrencyAccount_OLD GetCurrencyAccount(string currency)
        {
            if (CurrencyAccounts.TryGetValue(currency, out var account))
                return account;

            throw new NotSupportedException($"Not supported currency {currency}");
        }

        public ICurrencyAccount_OLD GetTezosTokenAccount(
            string currency,
            string tokenContract,
            decimal tokenId)
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

        public T GetCurrencyAccount<T>(string currency) where T : class, ICurrencyAccount_OLD =>
            GetCurrencyAccount(currency) as T;

        public T GetTezosTokenAccount<T>(
            string currency,
            string tokenContract,
            decimal tokenId) where T : class =>
            GetTezosTokenAccount(currency, tokenContract, tokenId) as T;

        public string GetUserId(uint keyIndex = 0)
        {
            using var servicePublicKey = Wallet.GetServicePublicKey(keyIndex);
            var publicKey = servicePublicKey.ToUnsecuredBytes();

            return HashAlgorithm.Sha256.Hash(publicKey, iterations: 2).ToHexString();
        }

        #endregion Common

        #region Balances

        public Task<Balance_OLD> GetBalanceAsync(
            string currency,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(GetCurrencyAccount(currency).GetBalance());
        }

        public Task<Balance_OLD> GetAddressBalanceAsync(
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

        public Task<IBlockchainTransaction_OLD> GetTransactionByIdAsync(
            string currency,
            string txId)
        {
            return DataRepository.GetTransactionByIdAsync(
                currency: currency,
                txId: txId,
                transactionType: Currencies.GetByName(currency).TransactionType);
        }

        public Task<IEnumerable<IBlockchainTransaction_OLD>> GetTransactionsAsync(string currency)
        {
            return DataRepository.GetTransactionsAsync(
                currency: currency,
                transactionType: Currencies.GetByName(currency).TransactionType);
        }

        public async Task<IEnumerable<IBlockchainTransaction_OLD>> GetTransactionsAsync()
        {
            var result = new List<IBlockchainTransaction_OLD>();

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