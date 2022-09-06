using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Atomex.Abstract;
using Atomex.Blockchain.Abstract;
using Atomex.Core;
using Atomex.Wallet.Bip;

namespace Atomex.Wallet.Abstract
{
    public abstract class CurrencyAccount : ICurrencyAccount
    {
        public event EventHandler<CurrencyEventArgs> BalanceUpdated;

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

        #region Common

        protected void RaiseBalanceUpdated(CurrencyEventArgs eventArgs)
        {
            BalanceUpdated?.Invoke(this, eventArgs);
        }

        #endregion Common

        #region Balances

        public virtual async Task<Balance> GetAddressBalanceAsync(
            string address,
            CancellationToken cancellationToken = default)
        {
            var walletAddress = await LocalStorage
                .GetWalletAddressAsync(Currency, address)
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

        public Task<WalletAddress> DivideAddressAsync(
            KeyIndex keyIndex,
            int keyType)
        {
            return DivideAddressAsync(
                account: keyIndex.Account,
                chain: keyIndex.Chain,
                index: keyIndex.Index,
                keyType: keyType);
        }

        public virtual Task<WalletAddress> DivideAddressAsync(
            uint account,
            uint chain,
            uint index,
            int keyType)
        {
            var currency = Currencies.GetByName(Currency);

            var walletAddress = Wallet.GetAddress(
                currency: currency,
                account: account,
                chain: chain,
                index: index,
                keyType: keyType);

            return Task.FromResult(walletAddress);
        }

        public virtual Task<WalletAddress> GetAddressAsync(
            string address,
            CancellationToken cancellationToken = default)
        {
            return LocalStorage.GetWalletAddressAsync(Currency, address);
        }

        public virtual Task<IEnumerable<WalletAddress>> GetUnspentAddressesAsync(
            CancellationToken cancellationToken = default)
        {
            return LocalStorage.GetUnspentAddressesAsync(Currency);
        }

        public virtual async Task<WalletAddress> GetFreeExternalAddressAsync(
            CancellationToken cancellationToken = default)
        {
            // for tezos and tezos tokens with standard keys different account are used
            if (Atomex.Currencies.IsTezosBased(Currency))
            {
                var lastActiveAccountAddress = await LocalStorage
                    .GetLastActiveWalletAddressByAccountAsync(
                        currency: Currency,
                        keyType: CurrencyConfig.StandardKey)
                    .ConfigureAwait(false);

                return await DivideAddressAsync(
                        account: lastActiveAccountAddress?.KeyIndex.Account + 1 ?? Bip44.DefaultAccount,
                        chain: Bip44.External,
                        index: Bip44.DefaultIndex,
                        keyType: CurrencyConfig.StandardKey)
                    .ConfigureAwait(false);
            }

            var lastActiveAddress = await LocalStorage
                .GetLastActiveWalletAddressAsync(
                    currency: Currency,
                    chain: Bip44.External,
                    keyType: CurrencyConfig.StandardKey)
                .ConfigureAwait(false);

            return await DivideAddressAsync(
                    account: Bip44.DefaultAccount,
                    chain: Bip44.External,
                    index: lastActiveAddress?.KeyIndex.Index + 1 ?? Bip44.DefaultIndex,
                    keyType: CurrencyConfig.StandardKey)
                .ConfigureAwait(false);
        }

        public Task<IEnumerable<WalletAddress>> GetAddressesAsync(
            CancellationToken cancellationToken = default)
        {
            return LocalStorage.GetAddressesAsync(Currency);
        }

        #endregion Addresses

        #region Transactions

        public abstract Task<IEnumerable<IBlockchainTransaction>> GetUnconfirmedTransactionsAsync(
            CancellationToken cancellationToken = default);

        #endregion Transactions
    }
}