using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using NBitcoin;
using Serilog;

using Atomex.Abstract;
using Atomex.Api;
using Atomex.Blockchain;
using Atomex.Blockchain.Abstract;
using Atomex.Common;
using Atomex.Common.Memory;
using Atomex.Core;
using Atomex.Cryptography;
using Atomex.LiteDb;
using Atomex.Wallet.Abstract;
using Atomex.Wallet.KeyStorage;
using Atomex.Wallet.Settings;
using Atomex.Wallets.Abstract;
using Network = Atomex.Core.Network;

namespace Atomex.Wallet
{
    public class Account : IAccount
    {
        public const string DefaultUserSettingsFileName = "user.config";

        private const string DefaultDataFileName = "data.db";
        private const string DefaultAccountKey = "Account:Default";
        private const string ApiVersion = "1.3";

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
        public IHdWallet Wallet { get; private set; }
        public ICurrencies Currencies { get; private set; }
        public ISymbols Symbols { get; private set; }
        public UserSettings UserSettings { get; private set; }

        private IAccountDataRepository DataRepository { get; set; }
        private IDictionary<string, ICurrencyAccount> CurrencyAccounts { get; set; }

        public Account(
            string pathToWallet,
            string mnemonic,
            Wordlist language,
            SecureString derivePassPhrase,
            SecureString password,
            ICurrencies currencies,
            ISymbols symbols,
            Network network,
            IAccountDataRepository dataRepository = null)
        {
            const int saltSize = 16;
            var salt = Rand.SecureRandomBytes(saltSize);

            using var keyPassword = DerivePasswordKey(password, salt);

            var wallet = new HdWallet(
                mnemonic: mnemonic,
                wordList: language,
                passPhrase: derivePassPhrase,
                network: network)
            {
                PathToWallet = pathToWallet
            };

            wallet.Encrypt(keyPassword);
            wallet.SaveToFile(pathToWallet, keyPassword, salt);

            Init(keyPassword: keyPassword,
                wallet: wallet,
                currencies: currencies,
                symbols: symbols,
                dataRepository: dataRepository);
        }

        private Account(
            string pathToWallet,
            SecureString password,
            ICurrenciesProvider currenciesProvider,
            ISymbolsProvider symbolsProvider,
            IAccountDataRepository dataRepository)
        {
            byte[] salt;
            SecureBytes keyPassword = null;
            IHdWallet wallet;

            try
            {
                salt = HdKeyStorage.ReadSalt(pathToWallet);
                keyPassword = DerivePasswordKey(password, salt);
                wallet = HdWallet.LoadFromFile(pathToWallet, keyPassword);

                Init(keyPassword: keyPassword,
                    wallet: wallet,
                    currencies: currenciesProvider.GetCurrencies(wallet.Network),
                    symbols: symbolsProvider.GetSymbols(wallet.Network),
                    dataRepository: dataRepository);
            }
            finally
            {
                keyPassword?.Dispose();
            }
        }

        private void Init(
            SecureBytes keyPassword,
            IHdWallet wallet,
            ICurrencies currencies,
            ISymbols symbols,
            IAccountDataRepository dataRepository)
        {
            Wallet = wallet ?? throw new ArgumentNullException(nameof(wallet));
            Currencies = currencies ?? throw new ArgumentNullException(nameof(currencies));
            Symbols = symbols ?? throw new ArgumentNullException(nameof(symbols));

            DataRepository = dataRepository;

            if (DataRepository == null)
            {
                DataRepository = new LiteDbAccountDataRepository(
                    $"{Path.GetDirectoryName(Wallet.PathToWallet)}/{DefaultDataFileName}",
                    keyPassword,
                    currencies);
            }

            CurrencyAccounts = Currencies
                .ToDictionary(
                    c => c.Name,
                    c => CurrencyAccountCreator.Create(
                        currency: c.Name,
                        wallet: Wallet,
                        dataRepository: DataRepository,
                        currencies: Currencies));

            InitUserSettings(keyPassword);
        }

        private void InitUserSettings(
            SecureBytes keyPassword)
        {
            var pathToSettings = $"{Path.GetDirectoryName(Wallet.PathToWallet)}/{DefaultUserSettingsFileName}";

            UserSettings = UserSettings.LoadFromFile(
                pathToFile: pathToSettings,
                keyPassword: keyPassword) ?? UserSettings.DefaultSettings;
        }

        private SecureBytes DerivePasswordKey(SecureString password, byte[] salt) =>
            new SecureBytes(Argon2id.Compute(password, salt, hashLength: 32));

        #region Common

        public void Lock()
        {
            Wallet.Lock();

            Locked?.Invoke(this, EventArgs.Empty);
        }

        public void Unlock(SecureString password)
        {
            var salt = HdKeyStorage.ReadSalt(Wallet.PathToWallet);
            using var keyPassword = DerivePasswordKey(password, salt);

            Wallet.Unlock(keyPassword);

            Unlocked?.Invoke(this, EventArgs.Empty);
        }

        public IAccount UseUserSettings(UserSettings userSettings)
        {
            UserSettings = userSettings;
            return this;
        }

        public void SaveUserSettingsToFile(string pathToFile, SecureString password)
        {
            byte[] salt;
            SecureBytes keyPassword = null;

            try
            {
                salt = HdKeyStorage.ReadSalt(Wallet.PathToWallet);
                keyPassword = DerivePasswordKey(password, salt);

                UserSettings.SaveToFile(pathToFile, keyPassword);
            }
            finally
            {
                keyPassword?.Dispose();
            }
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
            var publicKey = securePublicKey.ToUnsecuredBytes();

            var auth = new Auth
            {
                TimeStamp = DateTime.UtcNow,
                Nonce = nonce.Nonce,
                ClientNonce = Guid.NewGuid().ToString(),
                PublicKeyHex = publicKey.ToHexString(),
                Version = ApiVersion
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
            ISymbolsProvider symbolsProvider)
        {
            var pathToAccount = configuration[DefaultAccountKey];

            if (string.IsNullOrEmpty(pathToAccount))
            {
                Log.Error("Path to default account is null or empty");
                return null;
            }

            if (!File.Exists(PathEx.ToFullPath(pathToAccount)))
            {
                Log.Error("Default account not found");
                return null;
            }

            return LoadFromFile(pathToAccount, password, currenciesProvider, symbolsProvider);
        }

        public static Account LoadFromFile(
            string pathToAccount,
            SecureString password,
            ICurrenciesProvider currenciesProvider,
            ISymbolsProvider symbolsProvider,
            IAccountDataRepository dataRepository = null)
        {
            return new Account(pathToAccount, password, currenciesProvider, symbolsProvider, dataRepository);
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

        public Task<T> GetTransactionByIdAsync<T>(
            string currency,
            string txId)
            where T : IBlockchainTransaction
        {
            return DataRepository.GetTransactionByIdAsync<T>(
                currency: currency,
                txId: txId);
        }

        public Task<IEnumerable<IBlockchainTransaction>> GetTransactionsAsync(string currency)
        {
            return GetCurrencyAccount(currency).GetTransactionsAsync();
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

        public Task<bool> RemoveTransactionAsync(string currency, string txId)
        {
            return DataRepository.RemoveTransactionByIdAsync(currency, txId);
        }

        #endregion Transactions

        #region Orders

        public Task<bool> UpsertOrderAsync(Order order)
        {
            return DataRepository.UpsertOrderAsync(order);
        }

        public Task<Order> GetOrderByIdAsync(string clientOrderId)
        {
            return DataRepository.GetOrderByIdAsync(clientOrderId);
        }

        public Task<Order> GetOrderByIdAsync(long id)
        {
            return DataRepository.GetOrderByIdAsync(id);
        }

        #endregion Orders

        #region Swaps

        public Task<bool> AddSwapAsync(Swap swap)
        {
            return DataRepository.InsertSwapAsync(swap);
        }

        public Task<bool> UpdateSwapAsync(Swap swap)
        {
            return DataRepository.UpdateSwapAsync(swap);
        }

        public Task<Swap> GetSwapByIdAsync(long swapId)
        {
            return DataRepository.GetSwapByIdAsync(swapId);
        }

        public Task<IEnumerable<Swap>> GetSwapsAsync()
        {
            return DataRepository.GetSwapsAsync();
        }

        #endregion Swaps
    }
}