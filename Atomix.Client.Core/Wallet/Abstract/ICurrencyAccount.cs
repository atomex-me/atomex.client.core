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
    public interface ICurrencyAccount
    {
        event EventHandler<CurrencyEventArgs> BalanceUpdated;
        event EventHandler<TransactionEventArgs> UnconfirmedTransactionAdded;

        Currency Currency { get; }

        Task<Error> SendAsync(
            string to,
            decimal amount,
            decimal fee,
            decimal feePrice,
            CancellationToken cancellationToken = default(CancellationToken));

        Task<decimal> EstimateFeeAsync(
            decimal amount,
            CancellationToken cancellationToken = default(CancellationToken));

        Task AddUnconfirmedTransactionAsync(
            IBlockchainTransaction tx,
            string[] selfAddresses,
            bool notify = true,
            CancellationToken cancellationToken = default(CancellationToken));

        Task AddConfirmedTransactionAsync(
            IBlockchainTransaction tx,
            CancellationToken cancellationToken = default(CancellationToken));

        Task<decimal> GetBalanceAsync(
            string address,
            CancellationToken cancellationToken = default(CancellationToken));

        Task<decimal> GetBalanceAsync(
            CancellationToken cancellationToken = default(CancellationToken));

        Task<bool> IsAddressHasOperationsAsync(
            WalletAddress walletAddress,
            CancellationToken cancellationToken = default(CancellationToken));

        Task<IEnumerable<WalletAddress>> GetUnspentAddressesAsync(
            decimal requiredAmount,
            CancellationToken cancellationToken = default(CancellationToken));

        Task<WalletAddress> GetFreeInternalAddressAsync(
            CancellationToken cancellationToken = default(CancellationToken));

        Task<WalletAddress> GetFreeExternalAddressAsync(
            CancellationToken cancellationToken = default(CancellationToken));

        Task<WalletAddress> GetRefundAddressAsync(
            IEnumerable<WalletAddress> paymentAddresses,
            CancellationToken cancellationToken = default(CancellationToken));

        Task<WalletAddress> GetRedeemAddressAsync(
            CancellationToken cancellationToken = default(CancellationToken));

        void RaiseBalanceUpdated(CurrencyEventArgs eventArgs);
    }
}