using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Atomex.Abstract;
using Atomex.Blockchain;
using Atomex.Blockchain.Abstract;
using Atomex.Common;
using Atomex.Core;
using Atomex.Wallet.Bip;

namespace Atomex.Wallet.Abstract
{
    public abstract class CurrencyAccount : ICurrencyAccount
    {
        public event EventHandler<CurrencyEventArgs> BalanceUpdated;
        public event EventHandler<TransactionEventArgs> UnconfirmedTransactionAdded;

        public string Currency { get; }
        public ICurrencies Currencies { get; }
        public IHdWallet Wallet { get; }
        public IAccountDataRepository DataRepository { get; }
        protected decimal Balance { get; set; }
        protected decimal UnconfirmedIncome { get; set; }
        protected decimal UnconfirmedOutcome { get; set; }

        protected CurrencyAccount(
            string currency,
            ICurrencies currencies,
            IHdWallet wallet,
            IAccountDataRepository dataRepository)
        {
            Currency       = currency ?? throw new ArgumentNullException(nameof(currency));
            Currencies     = currencies ?? throw new ArgumentNullException(nameof(currencies));
            Wallet         = wallet ?? throw new ArgumentNullException(nameof(wallet));
            DataRepository = dataRepository ?? throw new ArgumentNullException(nameof(dataRepository));

            LoadBalances();
        }

        #region Common

        protected void RaiseBalanceUpdated(CurrencyEventArgs eventArgs)
        {
            BalanceUpdated?.Invoke(this, eventArgs);
        }

        protected void RaiseUnconfirmedTransactionAdded(TransactionEventArgs eventArgs)
        {
            UnconfirmedTransactionAdded?.Invoke(this, eventArgs);
        }

        protected virtual Task<bool> ResolveTransactionTypeAsync(
            IBlockchainTransaction tx,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(true);
        }

        protected async Task<bool> IsSelfAddressAsync(
            string address,
            CancellationToken cancellationToken = default)
        {
            var walletAddress = await GetAddressAsync(address, cancellationToken)
                .ConfigureAwait(false);

            return walletAddress != null;
        }

        #endregion Common

        #region Balances

        public virtual async Task<Balance> GetAddressBalanceAsync(
            string address,
            CancellationToken cancellationToken = default)
        {
            var walletAddress = await DataRepository
                .GetWalletAddressAsync(Currency, address)
                .ConfigureAwait(false);

            return walletAddress != null
                ? new Balance(
                    walletAddress.Balance,
                    walletAddress.UnconfirmedIncome,
                    walletAddress.UnconfirmedOutcome)
                : new Balance();
        }

        public virtual Balance GetBalance()
        {
            return new Balance(
                Balance,
                UnconfirmedIncome,
                UnconfirmedOutcome);
        }

        public abstract Task UpdateBalanceAsync(
            CancellationToken cancellationToken = default);

        public abstract Task UpdateBalanceAsync(
            string address,
            CancellationToken cancellationToken = default);

        protected void LoadBalances()
        {
            Balance            = 0;
            UnconfirmedIncome  = 0;
            UnconfirmedOutcome = 0;

            var addresses = DataRepository
                .GetUnspentAddressesAsync(Currency)
                .WaitForResult();

            foreach (var address in addresses)
            {
                Balance            += address.Balance;
                UnconfirmedIncome  += address.UnconfirmedIncome;
                UnconfirmedOutcome += address.UnconfirmedOutcome;
            }
        }

        #endregion Balances

        #region Addresses

        public virtual async Task<WalletAddress> DivideAddressAsync(
            int chain,
            uint index)
        {
            var currency = Currencies.GetByName(Currency);

            var walletAddress = Wallet.GetAddress(currency, chain, index);

            if (walletAddress == null)
                return null;

            await DataRepository
                .TryInsertAddressAsync(walletAddress)
                .ConfigureAwait(false);

            return walletAddress;
        }

        public virtual async Task<WalletAddress> GetAddressAsync(
            string address,
            CancellationToken cancellationToken = default)
        {
            var walletAddress = await DataRepository
                .GetWalletAddressAsync(Currency, address)
                .ConfigureAwait(false);

            return walletAddress != null
                ? ResolvePublicKey(walletAddress)
                : null;
        }

        public virtual Task<IEnumerable<WalletAddress>> GetUnspentAddressesAsync(
            CancellationToken cancellationToken = default)
        {
            return DataRepository.GetUnspentAddressesAsync(Currency);
        }

        protected WalletAddress ResolvePublicKey(WalletAddress address) =>
            address.ResolvePublicKey(Currencies, Wallet);

        protected IList<WalletAddress> ResolvePublicKeys(IList<WalletAddress> addresses) =>
            addresses.ResolvePublicKeys(Currencies, Wallet);

        public virtual async Task<WalletAddress> GetFreeInternalAddressAsync(
            CancellationToken cancellationToken = default)
        {
            var lastActiveAddress = await DataRepository
                .GetLastActiveWalletAddressAsync(
                    currency: Currency,
                    chain: Bip44.Internal)
                .ConfigureAwait(false);

            return await DivideAddressAsync(
                    chain: Bip44.Internal,
                    index: lastActiveAddress?.KeyIndex.Index + 1 ?? 0)
                .ConfigureAwait(false);
        }

        public virtual async Task<WalletAddress> GetFreeExternalAddressAsync(
            CancellationToken cancellationToken = default)
        {
            var lastActiveAddress = await DataRepository
                .GetLastActiveWalletAddressAsync(
                    currency: Currency,
                    chain: Bip44.External)
                .ConfigureAwait(false);

            return await DivideAddressAsync(
                    chain: Bip44.External,
                    index: lastActiveAddress?.KeyIndex.Index + 1 ?? 0)
                .ConfigureAwait(false);
        }

        public Task<IEnumerable<WalletAddress>> GetAddressesAsync(
            CancellationToken cancellationToken = default)
        {
            return DataRepository.GetAddressesAsync(Currency);
        }

        #endregion Addresses

        #region Transactions

        public virtual async Task UpsertTransactionAsync(
            IBlockchainTransaction tx,
            bool updateBalance = false,
            bool notifyIfUnconfirmed = true,
            bool notifyIfBalanceUpdated = true,
            CancellationToken cancellationToken = default)
        {
            var result = await ResolveTransactionTypeAsync(tx, cancellationToken)
                .ConfigureAwait(false);

            if (result == false)
                return;

            result = await DataRepository
                .UpsertTransactionAsync(tx)
                .ConfigureAwait(false);

            if (!result)
                return; // todo: error or message?

            if (updateBalance)
                await UpdateBalanceAsync(cancellationToken)
                    .ConfigureAwait(false);

            if (notifyIfUnconfirmed && !tx.IsConfirmed)
                RaiseUnconfirmedTransactionAdded(new TransactionEventArgs(tx));

            if (updateBalance && notifyIfBalanceUpdated)
                RaiseBalanceUpdated(new CurrencyEventArgs(tx.Currency));
        }

        #endregion Transactions
    }
}