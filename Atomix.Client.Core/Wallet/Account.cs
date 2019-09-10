using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security;
using System.Threading;
using System.Threading.Tasks;
using Atomix.Abstract;
using Atomix.Blockchain;
using Atomix.Blockchain.Abstract;
using Atomix.Common;
using Atomix.Core;
using Atomix.Core.Entities;
using Atomix.LiteDb;
using Atomix.Wallet.Abstract;
using Microsoft.Extensions.Configuration;
using Serilog;

namespace Atomix.Wallet
{
    public class Account : IAccount
    {
        public const string DefaultUserSettingsFileName = "user.config";

        private const string DefaultDataFileName = "data.db";
        private const string DefaultAccountKey = "Account:Default";
        private const string ApiVersion = "1.0";

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
        public ISymbols Symbols { get; }
        public UserSettings UserSettings { get; private set; }

        private IAccountDataRepository DataRepository { get; }
        private IDictionary<string, ICurrencyAccount> CurrencyAccounts { get; }

        private Account(
            string pathToAccount,
            SecureString password,
            ICurrenciesProvider currenciesProvider,
            ISymbolsProvider symbolsProvider)
            : this(wallet: HdWallet.LoadFromFile(pathToAccount, password),
                   password: password,
                   currenciesProvider: currenciesProvider,
                   symbolsProvider : symbolsProvider)
        {
        }

        public Account(
            IHdWallet wallet,
            SecureString password,
            ICurrenciesProvider currenciesProvider,
            ISymbolsProvider symbolsProvider)
        {
            Wallet = wallet ?? throw new ArgumentNullException(nameof(wallet));

            Currencies = currenciesProvider.GetCurrencies(Network);
            Symbols = symbolsProvider.GetSymbols(Network);

            DataRepository = new LiteDbAccountDataRepository(
                pathToDb: $"{Path.GetDirectoryName(Wallet.PathToWallet)}/{DefaultDataFileName}",
                password: password,
                currencies: Currencies,
                symbols: Symbols);

            CurrencyAccounts = Currencies
                .ToDictionary(
                    c => c.Name,
                    c => CurrencyAccountCreator.Create(c, Wallet, DataRepository));

            UserSettings = UserSettings.TryLoadFromFile(
                pathToFile: $"{Path.GetDirectoryName(Wallet.PathToWallet)}/{DefaultUserSettingsFileName}",
                password: password);

            if (UserSettings == null)
                UserSettings = UserSettings.DefaultSettings;
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
            Currency currency,
            IEnumerable<WalletAddress> from,
            string to,
            decimal amount,
            decimal fee,
            decimal feePrice,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            return GetAccountByCurrency(currency)
                .SendAsync(
                    from: from,
                    to: to,
                    amount: amount,
                    fee: fee,
                    feePrice: feePrice,
                    cancellationToken: cancellationToken);
        }

        public Task<Error> SendAsync(
            Currency currency,
            string to,
            decimal amount,
            decimal fee,
            decimal feePrice,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            return GetAccountByCurrency(currency)
                .SendAsync(
                    to: to,
                    amount: amount,
                    fee: fee,
                    feePrice: feePrice,
                    cancellationToken: cancellationToken);
        }

        public Task<decimal> EstimateFeeAsync(
            Currency currency,
            string to,
            decimal amount,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            return GetAccountByCurrency(currency)
                .EstimateFeeAsync(to, amount, cancellationToken);
        }

        public async Task<Auth> CreateAuthRequestAsync(AuthNonce nonce, uint keyIndex = 0)
        {
            if (IsLocked)
            {
                Log.Warning("Wallet locked");
                return null;
            }

            var servicePublicKey = Wallet.GetServicePublicKey(keyIndex);

            var auth = new Auth
            {
                TimeStamp = DateTime.UtcNow,
                Nonce = nonce.Nonce,
                ClientNonce = Guid.NewGuid().ToString(),
                PublicKeyHex = servicePublicKey.ToHexString(),
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
            ISymbolsProvider symbolsProvider)
        {
            return new Account(pathToAccount, password, currenciesProvider, symbolsProvider);
        }

        private ICurrencyAccount GetAccountByCurrency(Currency currency)
        {
            if (CurrencyAccounts.TryGetValue(currency.Name, out var account))
                return account;

            throw new NotSupportedException($"Not supported currency {currency.Name}");
        }

        #endregion Common

        #region Balances

        public Task<Balance> GetBalanceAsync(
            Currency currency,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            return Task.FromResult(GetAccountByCurrency(currency).GetBalance());
        }

        public Task<Balance> GetAddressBalanceAsync(
            Currency currency,
            string address,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            return GetAccountByCurrency(currency)
                .GetAddressBalanceAsync(address, cancellationToken);
        }

        public Task UpdateBalanceAsync(
            Currency currency,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            return GetAccountByCurrency(currency)
                .UpdateBalanceAsync(cancellationToken);
        }

        public Task UpdateBalanceAsync(
            Currency currency,
            string address,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            return GetAccountByCurrency(currency)
                .UpdateBalanceAsync(address, cancellationToken);
        }

        #endregion Balances

        #region Addresses

        public Task<WalletAddress> DivideAddressAsync(
            Currency currency,
            int chain,
            uint index)
        {
            return GetAccountByCurrency(currency)
                .DivideAddressAsync(chain, index);
        }

        public Task<WalletAddress> ResolveAddressAsync(
            Currency currency,
            string address,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            return GetAccountByCurrency(currency)
                .ResolveAddressAsync(address, cancellationToken);
        }

        public Task<IEnumerable<WalletAddress>> GetUnspentAddressesAsync(
            Currency currency,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            return GetAccountByCurrency(currency)
                .GetUnspentAddressesAsync(cancellationToken);
        }

        public Task<IEnumerable<WalletAddress>> GetUnspentAddressesAsync(
            Currency currency,
            decimal amount,
            decimal fee,
            decimal feePrice,
            bool isFeePerTransaction,
            AddressUsagePolicy addressUsagePolicy,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            return GetAccountByCurrency(currency)
                .GetUnspentAddressesAsync(
                    amount: amount,
                    fee: fee,
                    feePrice: feePrice,
                    isFeePerTransaction: isFeePerTransaction,
                    addressUsagePolicy: addressUsagePolicy,
                    cancellationToken: cancellationToken);
        }

        public Task<WalletAddress> GetFreeInternalAddressAsync(
            Currency currency,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            return GetAccountByCurrency(currency)
                .GetFreeInternalAddressAsync(cancellationToken);
        }

        public Task<WalletAddress> GetFreeExternalAddressAsync(
            Currency currency,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            return GetAccountByCurrency(currency)
                .GetFreeExternalAddressAsync(cancellationToken);
        }

        public Task<WalletAddress> GetRefundAddressAsync(
            Currency currency,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            return GetAccountByCurrency(currency)
                .GetRefundAddressAsync(cancellationToken);
        }

        public Task<WalletAddress> GetRedeemAddressAsync(
            Currency currency,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            return GetAccountByCurrency(currency)
                .GetRedeemAddressAsync(cancellationToken);
        }

        #endregion Addresses

        #region Transactions

        public Task UpsertTransactionAsync(
            IBlockchainTransaction tx,
            bool updateBalance = false,
            bool notifyIfUnconfirmed = true,
            bool notifyIfBalanceUpdated = true,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            return GetAccountByCurrency(tx.Currency)
                .UpsertTransactionAsync(
                    tx: tx,
                    updateBalance: updateBalance,
                    notifyIfUnconfirmed: notifyIfUnconfirmed,
                    notifyIfBalanceUpdated: notifyIfBalanceUpdated,
                    cancellationToken: cancellationToken);
        }

        public Task<IBlockchainTransaction> GetTransactionByIdAsync(
            Currency currency,
            string txId)
        {
            return DataRepository.GetTransactionByIdAsync(currency, txId);
        }

        public Task<IEnumerable<IBlockchainTransaction>> GetTransactionsAsync(Currency currency)
        {
            return DataRepository.GetTransactionsAsync(currency);
        }

        public async Task<IEnumerable<IBlockchainTransaction>> GetTransactionsAsync()
        {
            var result = new List<IBlockchainTransaction>();

            foreach (var currency in Currencies)
            {
                var txs = await GetTransactionsAsync(currency)
                    .ConfigureAwait(false);

                result.AddRange(txs);
            }

            return result;
        }

        #endregion Transactions

        #region Outputs

        public Task UpsertOutputsAsync(
            IEnumerable<ITxOutput> outputs,
            Currency currency,
            string address,
            bool notifyIfBalanceUpdated = true)
        {
            return GetAccountByCurrency(currency)
                .UpsertOutputsAsync(
                    outputs: outputs,
                    currency: currency,
                    address: address,
                    notifyIfBalanceUpdated: notifyIfBalanceUpdated);
        }

        public Task<IEnumerable<ITxOutput>> GetAvailableOutputsAsync(Currency currency)
        {
            return DataRepository.GetAvailableOutputsAsync(
                currency: currency);
        }

        public Task<IEnumerable<ITxOutput>> GetAvailableOutputsAsync(
            Currency currency,
            string address)
        {
            return DataRepository.GetAvailableOutputsAsync(
                currency: currency,
                address: address);
        }

        public Task<IEnumerable<ITxOutput>> GetOutputsAsync(Currency currency)
        {
            return DataRepository.GetOutputsAsync(currency);
        }

        public Task<IEnumerable<ITxOutput>> GetOutputsAsync(Currency currency, string address)
        {
            return DataRepository.GetOutputsAsync(currency, address);
        }

        #endregion Outputs

        #region Orders

        public Task<bool> UpsertOrderAsync(Order order)
        {
            return DataRepository.UpsertOrderAsync(order);
        }

        public Order GetOrderById(string clientOrderId)
        {
            return DataRepository.GetOrderById(clientOrderId);
        }

        #endregion Orders

        #region Swaps

        public Task<bool> AddSwapAsync(ClientSwap clientSwap)
        {
            return DataRepository.AddSwapAsync(clientSwap);
        }

        public Task<bool> UpdateSwapAsync(ClientSwap clientSwap)
        {
            return DataRepository.UpdateSwapAsync(clientSwap);
        }

        public Task<ClientSwap> GetSwapByIdAsync(long swapId)
        {
            return DataRepository.GetSwapByIdAsync(swapId);
        }

        public Task<IEnumerable<ClientSwap>> GetSwapsAsync()
        {
            return DataRepository.GetSwapsAsync();
        }

        #endregion Swaps
    }
}