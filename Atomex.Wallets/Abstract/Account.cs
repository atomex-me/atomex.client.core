using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;

using Atomex.Blockchain.Abstract;
using Atomex.Common;
using Atomex.Wallets.Common;

namespace Atomex.Wallets.Abstract
{
    public abstract class Account : IAccount
    {
        public string Currency { get; }
        protected IWalletProvider WalletProvider { get; set; }
        protected ICurrencyConfigProvider CurrencyConfigProvider { get; set; }
        public IWalletDataRepository DataRepository { get; }
        public ILogger Logger { get; }

        public Account(
            string currency,
            IWalletProvider walletProvider,
            ICurrencyConfigProvider currencyConfigProvider,
            IWalletDataRepository dataRepository,
            ILogger logger = null)
        {
            Currency = currency
                ?? throw new ArgumentNullException(nameof(currency));
            WalletProvider = walletProvider
                ?? throw new ArgumentNullException(nameof(walletProvider));
            CurrencyConfigProvider = currencyConfigProvider
                ?? throw new ArgumentNullException(nameof(currencyConfigProvider));
            DataRepository = dataRepository
                ?? throw new ArgumentNullException(nameof(dataRepository));

            Logger = logger;
        }

        //#region Wallets

        //public Task<WalletInfo> GetWalletByIdAsync(
        //    int walletId,
        //    CancellationToken cancellationToken)
        //{
        //    return DataRepository
        //        .GetWalletInfoByIdAsync(walletId, cancellationToken);
        //}

        //public Task<IEnumerable<WalletInfo>> GetWalletsAsync(
        //    CancellationToken cancellationToken = default)
        //{
        //    return DataRepository
        //        .GetWalletsInfoAsync(cancellationToken);
        //}

        //public Task<IEnumerable<WalletInfo>> GetWalletsAsync(
        //    string currency,
        //    CancellationToken cancellationToken = default)
        //{
        //    return DataRepository
        //        .GetWalletsInfoAsync(currency, cancellationToken);
        //}

        //#endregion Wallets

        #region Balances

        public abstract IWalletScanner GetWalletScanner();

        public virtual async Task<Balance> GetBalanceAsync(
            CancellationToken cancellationToken = default)
        {
            var wallets = await DataRepository
                .GetWalletsInfoAsync(Currency, cancellationToken)
                .ConfigureAwait(false);

            return wallets.Aggregate(Balance.ZeroNoUpdates, (result, wi) => result.Append(wi.Balance));
        }

        public virtual async Task<Balance> GetWalletBalanceAsync(
            int walletId,
            CancellationToken cancellationToken = default)
        {
            var walletInfo = await DataRepository
                .GetWalletInfoByIdAsync(walletId, cancellationToken)
                .ConfigureAwait(false);

            return walletInfo?.Balance ?? Balance.ZeroNoUpdates;
        }

        public virtual async Task<Balance> GetAddressBalanceAsync(
            string address,
            CancellationToken cancellationToken = default)
        {
            var walletAddress = await DataRepository
                .GetWalletAddressAsync(Currency, address, cancellationToken)
                .ConfigureAwait(false);

            return walletAddress?.Balance ?? Balance.ZeroNoUpdates;
        }

        public virtual async Task<(Balance balance, Error error)> UpdateBalanceAsync(
            bool skipUsedAddresses = true,
            CancellationToken cancellationToken = default)
        {
            var error = await GetWalletScanner()
                .ScanAsync(skipUsedAddresses, cancellationToken)
                .ConfigureAwait(false);

            if (error != null)
                return (balance: null, error);

            var balance = await GetBalanceAsync(cancellationToken)
                .ConfigureAwait(false);

            return (balance, error: null);
        }

        public virtual async Task<(Balance balance, Error error)> UpdateWalletBalanceAsync(
            int walletId,
            bool skipUsedAddresses = true,
            CancellationToken cancellationToken = default)
        {
            var error = await GetWalletScanner()
                .ScanAsync(walletId, skipUsedAddresses, cancellationToken)
                .ConfigureAwait(false);

            if (error != null)
                return (balance: null, error);

            var balance = await GetWalletBalanceAsync(walletId, cancellationToken)
                .ConfigureAwait(false);

            return (balance, error: null);
        }

        public virtual async Task<(Balance balance, Error error)> UpdateAddressBalanceAsync(
            string address,
            CancellationToken cancellationToken = default)
        {
            var error = await GetWalletScanner()
                .UpdateAddressBalanceAsync(address, cancellationToken)
                .ConfigureAwait(false);

            if (error != null)
                return (balance: null, error);

            var balance = await GetAddressBalanceAsync(address, cancellationToken)
                .ConfigureAwait(false);

            return (balance, error: null);
        }

        #endregion Balances

        #region Addresses

        //public virtual Task<bool> UpsertAddressAsync(
        //    WalletAddress walletAddress,
        //    CancellationToken cancellationToken = default)
        //{
        //    return DataRepository
        //        .UpsertAddressAsync(walletAddress, cancellationToken);
        //}

        public virtual Task<WalletAddress> GetAddressAsync(
            string address,
            CancellationToken cancellationToken = default)
        {
            return DataRepository
                .GetWalletAddressAsync(Currency, address, cancellationToken);
        }

        public virtual Task<IEnumerable<WalletAddress>> GetAddressesAsync(
            int walletId,
            int offset = 0,
            int limit = int.MaxValue,
            CancellationToken cancellationToken = default)
        {
            return DataRepository
                .GetAddressesAsync(Currency, walletId, offset, limit, cancellationToken);
        }

        public async Task<WalletAddress> GetFreeAddressAsync(
            int walletId,
            CancellationToken cancellationToken = default)
        {
            var walletInfo = await DataRepository
                .GetWalletInfoByIdAsync(walletId, cancellationToken)
                .ConfigureAwait(false);

            if (walletInfo == null)
                return null; // todo: error can't find wallet by id

            // use external chain if key pattern allows
            var keyPathPattern = walletInfo.KeyPathPattern.Replace(
                KeyPathExtensions.ChainPattern,
                KeyPathExtensions.ExternalChain);

            var lastActiveAddress = await DataRepository
                .GetLastActiveWalletAddressAsync(
                    currency: Currency,
                    walletId: walletId,
                    keyPathPattern: keyPathPattern,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            var keyPath = lastActiveAddress != null
                ? lastActiveAddress.KeyPath.SetIndex(
                    keyPathPattern: keyPathPattern,
                    indexPattern: KeyPathExtensions.IndexPattern,
                    indexValue: $"{lastActiveAddress.KeyIndex + 1}")
                : keyPathPattern
                    .Replace(KeyPathExtensions.AccountPattern, KeyPathExtensions.DefaultAccount)
                    .Replace(KeyPathExtensions.IndexPattern, KeyPathExtensions.DefaultIndex);

            return await DivideAddressAsync(
                    walletId: walletId,
                    keyPath: keyPath,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }

        private async Task<WalletAddress> DivideAddressAsync(
            int walletId,
            string keyPath,
            CancellationToken cancellationToken = default)
        {
            var walletInfo = await DataRepository
                .GetWalletInfoByIdAsync(walletId, cancellationToken)
                .ConfigureAwait(false);

            if (walletInfo == null)
                return null; // todo: error can't find wallet by id

            using var wallet = WalletProvider.GetWallet(walletInfo);

            if (wallet == null)
                return null; // todo: error can't find wallet by id

            using var publicKey = await wallet
                .GetPublicKeyAsync(keyPath, cancellationToken)
                .ConfigureAwait(false);

            var address = CurrencyConfigProvider
                .GetByName(Currency)
                .AddressFromKey(publicKey, walletInfo);

            var walletAddress = await DataRepository
                .GetWalletAddressAsync(Currency, address, cancellationToken)
                .ConfigureAwait(false);

            if (walletAddress != null)
                return walletAddress;

            walletAddress = new WalletAddress
            {
                Currency = Currency,
                Address  = address,
                Balance  = Balance.ZeroNoUpdates,
                WalletId = walletId,
                KeyPath  = keyPath,
                KeyIndex = keyPath.GetIndex(walletInfo.KeyPathPattern, KeyPathExtensions.IndexPattern)
            };

            var insertingResult = await DataRepository
                .TryInsertAddressAsync(walletAddress, cancellationToken)
                .ConfigureAwait(false);

            if (!insertingResult)
                return null; // todo: error can't insert address to data repository

            return walletAddress;
        }

        #endregion Addresses

        #region Transactions

        //public Task<bool> UpsertTransactionAsync<T>(
        //    T tx,
        //    CancellationToken cancellationToken = default)
        //    where T : Transaction
        //{
        //    return DataRepository
        //        .UpsertTransactionAsync(tx, cancellationToken);
        //}

        //public Task<int> UpsertTransactionsAsync<T>(
        //    IEnumerable<T> txs,
        //    CancellationToken cancellationToken = default)
        //    where T : Transaction
        //{
        //    return DataRepository
        //        .UpsertTransactionsAsync(txs, cancellationToken);
        //}

        public Task<T> GetTransactionByIdAsync<T>(
            string txId,
            CancellationToken cancellationToken = default)
            where T : Transaction
        {
            return DataRepository
                .GetTransactionByIdAsync<T>(Currency, txId, cancellationToken);
        }

        public Task<IEnumerable<T>> GetUnconfirmedTransactionsAsync<T>(
            int offset = 0,
            int limit = int.MaxValue,
            CancellationToken cancellationToken = default)
            where T : Transaction
        {
            return DataRepository
                .GetUnconfirmedTransactionsAsync<T>(Currency, offset, limit, cancellationToken);
        }

        public Task<bool> RemoveTransactionByIdAsync(
            string txId,
            CancellationToken cancellationToken = default)
        {
            return DataRepository
                .RemoveTransactionByIdAsync(Currency, txId, cancellationToken);
        }

        #endregion Transactions
    }
}