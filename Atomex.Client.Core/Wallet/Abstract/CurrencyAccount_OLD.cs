using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Serilog;

using Atomex.Abstract;
using Atomex.Blockchain;
using Atomex.Blockchain.Abstract;
using Atomex.Common;
using Atomex.Core;
using Atomex.Wallet.Bip;

namespace Atomex.Wallet.Abstract
{
    public abstract class CurrencyAccount_OLD : ICurrencyAccount_OLD, ITransactionalAccount
    {
        public event EventHandler<CurrencyEventArgs> BalanceUpdated;
        public event EventHandler<TransactionEventArgs> UnconfirmedTransactionAdded;

        public string Currency { get; }
        public ICurrencies Currencies { get; }
        public IHdWallet_OLD Wallet { get; }
        public IAccountDataRepository_OLD DataRepository { get; }
        protected decimal Balance { get; set; }
        protected decimal UnconfirmedIncome { get; set; }
        protected decimal UnconfirmedOutcome { get; set; }

        protected CurrencyAccount_OLD(
            string currency,
            ICurrencies currencies,
            IHdWallet_OLD wallet,
            IAccountDataRepository_OLD dataRepository)
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
            IBlockchainTransaction_OLD tx,
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

        public virtual async Task<Balance_OLD> GetAddressBalanceAsync(
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
                : new Balance_OLD();
        }

        public virtual Balance_OLD GetBalance()
        {
            return new Balance_OLD(
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

        public Task<WalletAddress_OLD> DivideAddressAsync(
            KeyIndex keyIndex,
            int keyType)
        {
            return DivideAddressAsync(
                account: keyIndex.Account,
                chain: keyIndex.Chain,
                index: keyIndex.Index,
                keyType: keyType);
        }

        public virtual async Task<WalletAddress_OLD> DivideAddressAsync(
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

            if (walletAddress == null)
                return null;

            _ = await DataRepository
                .TryInsertAddressAsync(walletAddress)
                .ConfigureAwait(false);

            return walletAddress;
        }

        public virtual async Task<WalletAddress_OLD> GetAddressAsync(
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

        public virtual Task<IEnumerable<WalletAddress_OLD>> GetUnspentAddressesAsync(
            CancellationToken cancellationToken = default)
        {
            return DataRepository.GetUnspentAddressesAsync(Currency);
        }

        protected WalletAddress_OLD ResolvePublicKey(WalletAddress_OLD address) =>
            address.ResolvePublicKey(Currencies, Wallet);

        protected IList<WalletAddress_OLD> ResolvePublicKeys(IList<WalletAddress_OLD> addresses) =>
            addresses.ResolvePublicKeys(Currencies, Wallet);

        public virtual async Task<WalletAddress_OLD> GetFreeExternalAddressAsync(
            CancellationToken cancellationToken = default)
        {
            // for tezos and tezos tokens with standard keys different account are used
            if (Atomex.Currencies.IsTezosBased(Currency))
            {
                var lastActiveAccountAddress = await DataRepository
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

            var lastActiveAddress = await DataRepository
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

        public Task<IEnumerable<WalletAddress_OLD>> GetAddressesAsync(
            CancellationToken cancellationToken = default)
        {
            return DataRepository.GetAddressesAsync(Currency);
        }

        #endregion Addresses

        #region Transactions

        public virtual async Task UpsertTransactionAsync(
            IBlockchainTransaction_OLD tx,
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
            {
                Log.Error("Tx upsert error.");
                return; // todo: error or message?
            }

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