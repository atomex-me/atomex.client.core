using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Atomex.Abstract;
using Atomex.Blockchain.Abstract;
using Atomex.Common;
using Atomex.Core;
using Atomex.Wallets.Bips;

namespace Atomex.Wallet.Abstract
{
    public abstract class CurrencyAccount : ICurrencyAccount
    {
        public string Currency { get; }
        public ICurrencies Currencies { get; }
        public IHdWallet Wallet { get; }
        public ILocalStorage LocalStorage { get; }

        protected CurrencyAccount(
            string currency,
            ICurrencies currencies,
            IHdWallet wallet,
            ILocalStorage localStorage)
        {
            Currency     = currency ?? throw new ArgumentNullException(nameof(currency));
            Currencies   = currencies ?? throw new ArgumentNullException(nameof(currencies));
            Wallet       = wallet ?? throw new ArgumentNullException(nameof(wallet));
            LocalStorage = localStorage ?? throw new ArgumentNullException(nameof(localStorage));
        }

        #region Balances

        public virtual async Task<Balance> GetAddressBalanceAsync(
            string address,
            CancellationToken cancellationToken = default)
        {
            var walletAddress = await LocalStorage
                .GetWalletAddressAsync(
                    currency: Currency,
                    address: address,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            return walletAddress != null
                ? new Balance(
                    walletAddress.Balance,
                    walletAddress.UnconfirmedIncome,
                    walletAddress.UnconfirmedOutcome)
                : new Balance();
        }

        public virtual async Task<Balance> GetBalanceAsync()
        {
            var unspentAddresses = await LocalStorage
                .GetUnspentAddressesAsync(Currency)
                .ConfigureAwait(false);

            var totalBalance = new Balance();

            foreach (var unspentAddress in unspentAddresses)
            {
                totalBalance.Confirmed += unspentAddress.Balance;
                totalBalance.UnconfirmedIncome += unspentAddress.UnconfirmedIncome;
                totalBalance.UnconfirmedOutcome += unspentAddress.UnconfirmedOutcome;
            }

            return totalBalance;
        }

        public abstract Task UpdateBalanceAsync(
            CancellationToken cancellationToken = default);

        public abstract Task UpdateBalanceAsync(
            string address,
            CancellationToken cancellationToken = default);

        #endregion Balances

        #region Addresses

        public virtual Task<WalletAddress> DivideAddressAsync(
            string keyPath,
            int keyType)
        {
            var currency = Currencies.GetByName(Currency);

            var walletAddress = Wallet.GetAddress(
                currency: currency,
                keyPath: keyPath,
                keyType: keyType);

            return Task.FromResult(walletAddress);
        }

        public virtual Task<WalletAddress> GetAddressAsync(
            string address,
            CancellationToken cancellationToken = default)
        {
            return LocalStorage.GetWalletAddressAsync(
                currency: Currency,
                address: address,
                cancellationToken: cancellationToken);
        }

        public virtual Task<IEnumerable<WalletAddress>> GetUnspentAddressesAsync(
            CancellationToken cancellationToken = default)
        {
            return LocalStorage.GetUnspentAddressesAsync(
                currency: Currency,
                includeUnconfirmed: true,
                cancellationToken: cancellationToken);
        }

        public virtual Task<WalletAddress> GetFreeExternalAddressAsync(
            CancellationToken cancellationToken = default)
        {
            return GetFreeAddressAsync(
                keyType: CurrencyConfig.StandardKey,
                chain: Bip44.External,
                cancellationToken: cancellationToken);
        }

        protected async Task<WalletAddress> GetFreeAddressAsync(
            int keyType,
            uint chain,
            CancellationToken cancellationToken = default)
        {
            var currency = Currencies.GetByName(Currency);

            var keyPathPattern = currency
                .GetKeyPathPattern(keyType)
                .Replace(KeyPathExtensions.ChainPattern, chain.ToString());

            var lastActiveAddress = await LocalStorage
                .GetLastActiveWalletAddressAsync(
                    currency: Currency,
                    keyPathPattern: keyPathPattern,
                    keyType: keyType,
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

            var freeAddress = await DivideAddressAsync(
                    keyPath: keyPath,
                    keyType: keyType)
                .ConfigureAwait(false);

            _ = await LocalStorage
                .UpsertAddressAsync(freeAddress, cancellationToken)
                .ConfigureAwait(false);

            return freeAddress;
        }

        public Task<IEnumerable<WalletAddress>> GetAddressesAsync(
            CancellationToken cancellationToken = default)
        {
            return LocalStorage.GetAddressesAsync(
                currency: Currency,
                cancellationToken: cancellationToken);
        }

        #endregion Addresses

        #region Transactions

        public abstract Task<IEnumerable<ITransaction>> GetUnconfirmedTransactionsAsync(
            CancellationToken cancellationToken = default);

        public abstract Task<ITransactionMetadata> ResolveTransactionMetadataAsync(
            ITransaction tx,
            CancellationToken cancellationToken = default);

        public abstract Task ResolveTransactionsMetadataAsync(
            IEnumerable<ITransaction> txs,
            CancellationToken cancellationToken = default);

        #endregion Transactions
    }
}