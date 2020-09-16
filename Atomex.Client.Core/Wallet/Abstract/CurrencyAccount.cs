using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Atomex.Abstract;
using Atomex.Blockchain;
using Atomex.Blockchain.Abstract;
using Atomex.Common;
using Atomex.Core;
using Atomex.Wallet.Bips;
using Atomex.Wallets.Abstract;

namespace Atomex.Wallet.Abstract
{
    public abstract class CurrencyAccount : ICurrencyAccount
    {
        public event EventHandler<CurrencyEventArgs> BalanceUpdated;
        public event EventHandler<TransactionEventArgs> UnconfirmedTransactionAdded;

        public string Currency { get; }
        public ICurrencies Currencies { get; }
        public IHdWallet Wallet { get; }
        protected IAccountDataRepository DataRepository { get; }
        protected decimal Balance { get; set; }
        protected decimal UnconfirmedIncome { get; set; }
        protected decimal UnconfirmedOutcome { get; set; }

        protected CurrencyAccount(
            string currency,
            ICurrencies currencies,
            IHdWallet wallet,
            IAccountDataRepository dataRepository)
        {
            Currency = currency ?? throw new ArgumentNullException(nameof(currency));
            Currencies = currencies ?? throw new ArgumentNullException(nameof(currencies));
            Wallet = wallet ?? throw new ArgumentNullException(nameof(wallet));
            DataRepository = dataRepository ?? throw new ArgumentNullException(nameof(dataRepository));

            PreloadBalances();
        }

        #region Common

        public abstract Task<Error> SendAsync(
            IEnumerable<WalletAddress> from,
            string to,
            decimal amount,
            decimal fee,
            decimal feePrice,
            bool useDefaultFee = false,
            CancellationToken cancellationToken = default);

        public abstract Task<Error> SendAsync(
            string to,
            decimal amount,
            decimal fee,
            decimal feePrice,
            bool useDefaultFee = false,
            CancellationToken cancellationToken = default);

        public abstract Task<decimal?> EstimateFeeAsync(
            string to,
            decimal amount,
            BlockchainTransactionType type,
            decimal fee = 0,
            decimal feePrice = 0,
            CancellationToken cancellationToken = default);

        public abstract Task<(decimal, decimal, decimal)> EstimateMaxAmountToSendAsync(
            string to,
            BlockchainTransactionType type,
            decimal fee = 0,
            decimal feePrice = 0,
            bool reserve = false,
            CancellationToken cancellationToken = default);

        public virtual Task<decimal> EstimateMaxFeeAsync(
            string to,
            decimal amount,
            BlockchainTransactionType type,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(0m);
        }

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

        private async void PreloadBalances()
        {
            try
            {
                var addresses = (await DataRepository
                    .GetUnspentAddressesAsync(Currency)
                    .ConfigureAwait(false))
                    .ToList();

                foreach (var address in addresses)
                {
                    Balance += address.Balance;
                    UnconfirmedIncome += address.UnconfirmedIncome;
                    UnconfirmedOutcome += address.UnconfirmedOutcome;
                }
            }
            catch (Exception e)
            {

            }
        }

        #endregion Balances

        #region Addresses

        public virtual async Task<WalletAddress> DivideAddressAsync(
            int chain,
            uint index,
            CancellationToken cancellationToken = default)
        {
            var currency = Currencies.GetByName(Currency);

            var walletAddress = Wallet.GetAddress(currency, chain, index);

            if (walletAddress == null)
                return null;

            await DataRepository.TryInsertAddressAsync(walletAddress)
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

            if (walletAddress != null)
            {
                var currency = Currencies.GetByName(Currency);

                walletAddress.PublicKey = Wallet
                    .GetAddress(currency, walletAddress.KeyIndex.Chain, walletAddress.KeyIndex.Index)
                    .PublicKey;
            }

            return walletAddress;
        }

        public virtual Task<IEnumerable<WalletAddress>> GetUnspentAddressesAsync(
            CancellationToken cancellationToken = default)
        {
            return DataRepository.GetUnspentAddressesAsync(Currency);
        }

        public virtual Task<IEnumerable<WalletAddress>> GetUnspentTokenAddressesAsync(
            CancellationToken cancellationToken = default)
        {
            return null;
        }

        public abstract Task<IEnumerable<WalletAddress>> GetUnspentAddressesAsync(
            string toAddress,
            decimal amount,
            decimal fee,
            decimal feePrice,
            FeeUsagePolicy feeUsagePolicy,
            AddressUsagePolicy addressUsagePolicy,
            BlockchainTransactionType transactionType,
            CancellationToken cancellationToken = default);

        public abstract Task<IEnumerable<SelectedWalletAddress>> SelectUnspentAddressesAsync(
            IList<WalletAddress> from,
            string to,
            decimal amount,
            decimal fee,
            decimal feePrice,
            FeeUsagePolicy feeUsagePolicy,
            AddressUsagePolicy addressUsagePolicy,
            BlockchainTransactionType transactionType,
            CancellationToken cancellationToken = default);

        protected WalletAddress ResolvePublicKey(WalletAddress address)
        {
            var currency = Currencies.GetByName(Currency);

            address.PublicKey = Wallet.GetAddress(
                    currency: currency,
                    chain: address.KeyIndex.Chain,
                    index: address.KeyIndex.Index)
                .PublicKey;

            return address;
        }

        protected IList<WalletAddress> ResolvePublicKeys(IList<WalletAddress> addresses)
        {
            foreach (var address in addresses)
                ResolvePublicKey(address);

            return addresses;
        }

        protected IEnumerable<WalletAddress> ApplyAddressUsagePolicy(
            List<WalletAddress> addresses,
            decimal amount,
            decimal fee,
            decimal feePrice,
            AddressUsagePolicy addressUsagePolicy)
        {
            var currency = Currencies.GetByName(Currency);

            switch (addressUsagePolicy) 
            {
                case AddressUsagePolicy.UseMinimalBalanceFirst:
                    addresses = addresses.SortList(new AvailableBalanceAscending());
                    break;
                case AddressUsagePolicy.UseMaximumBalanceFirst:
                    addresses = addresses.SortList(new AvailableBalanceDescending());
                    break;
                case AddressUsagePolicy.UseOnlyOneAddress:
                    var walletAddress = addresses
                        .FirstOrDefault(w => w.AvailableBalance() >= amount + currency.GetFeeAmount(fee, feePrice));

                    return walletAddress != null
                        ? new List<WalletAddress> { walletAddress }
                        : Enumerable.Empty<WalletAddress>();

                default:
                    throw new Exception("Address usage policy not supported");
            }

            return addresses;
        }

        public virtual async Task<WalletAddress> GetRedeemAddressAsync(
            CancellationToken cancellationToken = default)
        {
            var redeemAddress = await GetFreeExternalAddressAsync(cancellationToken)
                .ConfigureAwait(false);

            return ResolvePublicKey(redeemAddress);
        }

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
                    index: lastActiveAddress?.KeyIndex.Index + 1 ?? 0,
                    cancellationToken: cancellationToken)
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
                    index: lastActiveAddress?.KeyIndex.Index + 1 ?? 0,
                    cancellationToken: cancellationToken)
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
                RaiseBalanceUpdated(new CurrencyEventArgs(tx.Currency.Name));
        }

        public Task<T> GetTransactionByIdAsync<T>(string txId) where T : IBlockchainTransaction
        {
            return DataRepository.GetTransactionByIdAsync<T>(
                currency: Currency,
                txId: txId);
        }

        public abstract Task<IEnumerable<IBlockchainTransaction>> GetTransactionsAsync();

        #endregion Transactions
    }
}