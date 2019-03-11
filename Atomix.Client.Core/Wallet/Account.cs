using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security;
using System.Threading;
using System.Threading.Tasks;
using Atomix.Blockchain;
using Atomix.Blockchain.Abstract;
using Atomix.Common;
using Atomix.Core;
using Atomix.Core.Abstract;
using Atomix.Core.Entities;
using Atomix.LiteDb;
using Atomix.Swaps.Abstract;
using Atomix.Wallet.Abstract;
using Atomix.Wallet.CurrencyAccount;
using Microsoft.Extensions.Configuration;
using Serilog;

namespace Atomix.Wallet
{
    public class Account : IAccount
    {
        public const string DefaultHistoryFileName = "history.db";
        public const string DefaultAccountKey = "Account:Default";

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

        public event EventHandler SwapsLoaded;
        public event EventHandler Locked;
        public event EventHandler Unlocked;

        public IHdWallet Wallet { get; }
        private ITransactionRepository TransactionRepository { get; }
        private IOrderRepository OrderRepository { get; }
        private ISwapRepository SwapRepository { get; }

        public bool IsLocked => Wallet.IsLocked;
        public IEnumerable<Currency> Currencies => Wallet.Currencies;

        private IDictionary<string, ICurrencyAccount> CurrencyAccounts { get; }

        public Account(string pathToAccount, SecureString password)
            : this(new HdWallet(pathToAccount, password), password)
        {
        }

        public Account(IHdWallet wallet, SecureString password)
        {
            Wallet = wallet ?? throw new ArgumentNullException(nameof(wallet));

            var accountDirectory = Path.GetDirectoryName(Wallet.PathToWallet);

            TransactionRepository = new LiteDbTransactionRepository(
                pathToDb: $"{accountDirectory}\\{DefaultHistoryFileName}",
                password: password);

            OrderRepository = new LiteDbOrderRepository(
                pathToDb: $"{accountDirectory}\\{DefaultHistoryFileName}",
                password: password);

            SwapRepository = new LiteDbSwapRepository(
                pathToDb: $"{accountDirectory}\\{DefaultHistoryFileName}",
                password: password);

            CurrencyAccounts = Currencies
                .ToDictionary(
                    c => c.Name,
                    c => CurrencyAccountCreator.Create(c, Wallet, TransactionRepository));
        }

        public static IAccount LoadFromConfiguration(
            IConfiguration configuration,
            SecureString password)
        {
            var pathToAccount = configuration[DefaultAccountKey];

            if (string.IsNullOrEmpty(pathToAccount)) {
                Log.Error("Path to default account is null or empty");
                return null;
            }

            if (!File.Exists(PathEx.ToFullPath(pathToAccount))) {
                Log.Error("Default account not found");
                return null;
            }

            return new Account(pathToAccount, password);
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
            decimal amount,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            return GetAccountByCurrency(currency)
                .EstimateFeeAsync(amount, cancellationToken);
        }

        public async Task AddUnconfirmedTransactionAsync(
            IBlockchainTransaction tx,
            string[] selfAddresses,
            bool notify = true,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            await GetAccountByCurrency(tx.Currency)
                .AddUnconfirmedTransactionAsync(tx, selfAddresses, notify, cancellationToken)
                .ConfigureAwait(false);
        }

        public async Task AddConfirmedTransactionAsync(
            IBlockchainTransaction tx,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            await GetAccountByCurrency(tx.Currency)
                .AddConfirmedTransactionAsync(tx, cancellationToken)
                .ConfigureAwait(false);
        }

        public Task<decimal> GetBalanceAsync(
            Currency currency,
            string address,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            return GetAccountByCurrency(currency)
                .GetBalanceAsync(address, cancellationToken);
        }

        public Task<decimal> GetBalanceAsync(
            Currency currency,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            return GetAccountByCurrency(currency)
                .GetBalanceAsync(cancellationToken);
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

        private ICurrencyAccount GetAccountByCurrency(Currency currency)
        {
            if (CurrencyAccounts.TryGetValue(currency.Name, out var account))
                return account;

            throw new NotSupportedException($"Not supported currency {currency.Name}");
        }

        #region Wallet

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

        public WalletAddress GetAddress(
            Currency currency,
            uint chain,
            uint index)
        {
            return Wallet.GetAddress(currency, chain, index);
        }

        public Task<IEnumerable<WalletAddress>> GetUnspentAddressesAsync(
            Currency currency,
            decimal requiredAmount,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            return GetAccountByCurrency(currency)
                .GetUnspentAddressesAsync(requiredAmount, cancellationToken);
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

        #endregion Wallet

        #region Transactions Proxy

        public async Task<bool> AddTransactionAsync(IBlockchainTransaction tx)
        {
            var result = await TransactionRepository
                .AddTransactionAsync(tx)
                .ConfigureAwait(false);

            GetAccountByCurrency(tx.Currency)
                .RaiseBalanceUpdated(new CurrencyEventArgs(tx.Currency));

            return result;
        }

        public async Task<bool> AddOutputsAsync(
            IEnumerable<ITxOutput> outputs,
            Currency currency,
            string address)
        {
            var result = await TransactionRepository
                .AddOutputsAsync(outputs, currency, address)
                .ConfigureAwait(false);

            GetAccountByCurrency(currency)
                .RaiseBalanceUpdated(new CurrencyEventArgs(currency));

            return result;
        }

        public Task<IBlockchainTransaction> GetTransactionByIdAsync(
            Currency currency,
            string txId)
        {
            return TransactionRepository.GetTransactionByIdAsync(currency, txId);
        }

        public Task<IEnumerable<IBlockchainTransaction>> GetTransactionsAsync(Currency currency)
        {
            return TransactionRepository.GetTransactionsAsync(currency);
        }

        public Task<IEnumerable<ITxOutput>> GetUnspentOutputsAsync(
            Currency currency,
            bool skipUnconfirmed = true)
        {
            return TransactionRepository.GetUnspentOutputsAsync(
                currency,
                skipUnconfirmed);
        }

        public Task<IEnumerable<ITxOutput>> GetUnspentOutputsAsync(
            Currency currency,
            string address,
            bool skipUnconfirmed = true)
        {
            return TransactionRepository.GetUnspentOutputsAsync(
                currency,
                address,
                skipUnconfirmed);
        }

        public Task<IEnumerable<ITxOutput>> GetOutputsAsync(Currency currency)
        {
            return TransactionRepository.GetOutputsAsync(currency);
        }

        public Task<IEnumerable<ITxOutput>> GetOutputsAsync(
            Currency currency,
            string address)
        {
            return TransactionRepository.GetOutputsAsync(currency, address);
        }

        #endregion Transactions Proxy

        #region Orders Proxy

        public Task<bool> AddOrderAsync(Order order)
        {
            return OrderRepository.AddOrderAsync(order);
        }

        #endregion Orders Proxy

        #region Swaps Proxy

        public Task<bool> AddSwapAsync(ISwap swap)
        {
            return SwapRepository.AddSwapAsync(swap);
        }

        public Task<bool> UpdateSwapAsync(ISwap swap)
        {
            return SwapRepository.UpdateSwapAsync(swap);
        }

        public Task<bool> RemoveSwapAsync(ISwap swap)
        {
            return SwapRepository.RemoveSwapAsync(swap);
        }

        public Task<ISwap> GetSwapByIdAsync(Guid swapId)
        {
            return SwapRepository.GetSwapByIdAsync(swapId);
        }

        public Task<IEnumerable<ISwap>> GetSwapsAsync()
        {
            return SwapRepository.GetSwapsAsync();
        }

        public async Task LoadSwapsAsync()
        {
            var _ = await SwapRepository
                .GetSwapsAsync()
                .ConfigureAwait(false);

            SwapsLoaded?.Invoke(this, EventArgs.Empty);
        }

        #endregion Swaps Proxy

        public async Task<Auth> CreateAuthRequestAsync(
            AuthNonce nonce,
            uint keyIndex = 0)
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
                PublicKeyHex = servicePublicKey.ToHexString()
            };

            var signature = await Wallet
                .SignByServiceKeyAsync(auth.SignedData, keyIndex)
                .ConfigureAwait(false);

            auth.Signature = Convert.ToBase64String(signature);

            return auth;
        }
    }
}