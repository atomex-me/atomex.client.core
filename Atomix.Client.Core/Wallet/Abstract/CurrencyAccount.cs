using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Atomix.Blockchain;
using Atomix.Blockchain.Abstract;
using Atomix.Core;
using Atomix.Core.Entities;

namespace Atomix.Wallet.Abstract
{
    public abstract class CurrencyAccount : ICurrencyAccount
    {
        public const int MinFreeAddress = 3;

        public event EventHandler<CurrencyEventArgs> BalanceUpdated;
        public event EventHandler<TransactionEventArgs> UnconfirmedTransactionAdded;

        public Currency Currency { get; }

        protected IHdWallet Wallet { get; set; }
        protected ITransactionRepository TransactionRepository { get; set; }

        protected CurrencyAccount(
            Currency currency,
            IHdWallet wallet,
            ITransactionRepository transactionRepository)
        {
            Currency = currency;
            Wallet = wallet;
            TransactionRepository = transactionRepository;
        }

        public abstract Task<Error> SendAsync(
            string to,
            decimal amount,
            decimal fee,
            decimal feePrice,
            CancellationToken cancellationToken = default(CancellationToken));

        public abstract Task<decimal> EstimateFeeAsync(
            decimal amount,
            CancellationToken cancellationToken = default(CancellationToken));

        public abstract Task AddUnconfirmedTransactionAsync(
            IBlockchainTransaction tx,
            string[] selfAddresses,
            bool notify = true,
            CancellationToken cancellationToken = default(CancellationToken));

        public abstract Task AddConfirmedTransactionAsync(
            IBlockchainTransaction tx,
            CancellationToken cancellationToken = default(CancellationToken));

        public abstract Task<decimal> GetBalanceAsync(
            string address,
            CancellationToken cancellationToken = default(CancellationToken));

        public abstract Task<decimal> GetBalanceAsync(
            CancellationToken cancellationToken = default(CancellationToken));

        public abstract Task<bool> IsAddressHasOperationsAsync(
            WalletAddress walletAddress,
            CancellationToken cancellationToken = default(CancellationToken));

        public abstract Task<IEnumerable<WalletAddress>> GetUnspentAddressesAsync(
            decimal requiredAmount,
            CancellationToken cancellationToken = default(CancellationToken));

        public abstract Task<WalletAddress> GetRefundAddressAsync(
            IEnumerable<WalletAddress> paymentAddresses,
            CancellationToken cancellationToken = default(CancellationToken));

        public abstract Task<WalletAddress> GetRedeemAddressAsync(
            CancellationToken cancellationToken = default(CancellationToken));

        public virtual async Task<WalletAddress> GetFreeInternalAddressAsync(
            CancellationToken cancellationToken = default(CancellationToken))
        {
            var index = 0u;
            var freeIndexes = 0u;

            while (true)
            {
                var walletAddress = Wallet.GetInternalAddress(Currency, index);

                if (!await IsAddressHasOperationsAsync(walletAddress)) {
                    freeIndexes++;
                } else {
                    freeIndexes = 0;
                }

                if (freeIndexes == MinFreeAddress)
                    return Wallet.GetInternalAddress(Currency, index - freeIndexes + 1);

                index++;
            }
        }

        public virtual async Task<WalletAddress> GetFreeExternalAddressAsync(
            CancellationToken cancellationToken = default(CancellationToken))
        {
            var index = 0u;
            var freeIndexes = 0u;

            while (true)
            {
                var walletAddress = Wallet.GetExternalAddress(Currency, index);

                if (!await IsAddressHasOperationsAsync(walletAddress)) {
                    freeIndexes++;
                } else {
                    freeIndexes = 0;
                }

                if (freeIndexes == MinFreeAddress)
                    return Wallet.GetExternalAddress(Currency, index - freeIndexes + 1);

                index++;
            }
        }

        public void RaiseBalanceUpdated(CurrencyEventArgs eventArgs)
        {
            BalanceUpdated?.Invoke(this, eventArgs);
        }

        protected void RaiseUnconfirmedTransactionAdded(TransactionEventArgs eventArgs)
        {
            UnconfirmedTransactionAdded?.Invoke(this, eventArgs);
        }
    }
}