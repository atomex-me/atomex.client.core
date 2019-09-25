using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Atomex.Blockchain;
using Atomex.Blockchain.Abstract;
using Atomex.Core;
using Atomex.Core.Entities;

namespace Atomex.Wallet.Abstract
{
    public interface ICurrencyAccount
    {
        event EventHandler<CurrencyEventArgs> BalanceUpdated;
        event EventHandler<TransactionEventArgs> UnconfirmedTransactionAdded;

        #region Common

        Task<Error> SendAsync(
            IEnumerable<WalletAddress> from,
            string to,
            decimal amount,
            decimal fee,
            decimal feePrice,
            CancellationToken cancellationToken = default(CancellationToken));

        Task<Error> SendAsync(
            string to,
            decimal amount,
            decimal fee,
            decimal feePrice,
            CancellationToken cancellationToken = default(CancellationToken));

        Task<decimal?> EstimateFeeAsync(
            string to,
            decimal amount,
            BlockchainTransactionType type,
            CancellationToken cancellationToken = default(CancellationToken));

        Task<(decimal, decimal)> EstimateMaxAmountToSendAsync(
            string to,
            BlockchainTransactionType type,
            CancellationToken cancellationToken = default(CancellationToken));

        #endregion Common

        #region Balances

        Balance GetBalance();

        Task<Balance> GetAddressBalanceAsync(
            string address,
            CancellationToken cancellationToken = default(CancellationToken));

        Task UpdateBalanceAsync(
            CancellationToken cancellationToken = default(CancellationToken));

        Task UpdateBalanceAsync(
            string address,
            CancellationToken cancellationToken = default(CancellationToken));

        #endregion Balances

        #region Addresses

        Task<WalletAddress> DivideAddressAsync(
            int chain,
            uint index,
            CancellationToken cancellationToken = default(CancellationToken));

        Task<WalletAddress> ResolveAddressAsync(
            string address,
            CancellationToken cancellationToken = default(CancellationToken));

        Task<IEnumerable<WalletAddress>> GetUnspentAddressesAsync(
            CancellationToken cancellationToken = default(CancellationToken));

        Task<IEnumerable<WalletAddress>> GetUnspentAddressesAsync(
            decimal amount,
            decimal fee,
            decimal feePrice,
            FeeUsagePolicy feeUsagePolicy,
            AddressUsagePolicy addressUsagePolicy,
            BlockchainTransactionType transactionType,
            CancellationToken cancellationToken = default(CancellationToken));

        Task<WalletAddress> GetFreeInternalAddressAsync(
            CancellationToken cancellationToken = default(CancellationToken));

        Task<WalletAddress> GetFreeExternalAddressAsync(
            CancellationToken cancellationToken = default(CancellationToken));

        Task<WalletAddress> GetRefundAddressAsync(
            CancellationToken cancellationToken = default(CancellationToken));

        Task<WalletAddress> GetRedeemAddressAsync(
            CancellationToken cancellationToken = default(CancellationToken));

        #endregion Addresses

        #region Transactions

        Task UpsertTransactionAsync(
            IBlockchainTransaction tx,
            bool updateBalance = false,
            bool notifyIfUnconfirmed = true,
            bool notifyIfBalanceUpdated = true,
            CancellationToken cancellationToken = default(CancellationToken));

        #endregion Transactions

        #region Outputs

        Task UpsertOutputsAsync(
            IEnumerable<ITxOutput> outputs,
            Currency currency,
            string address,
            bool notifyIfBalanceUpdated = true,
            CancellationToken cancellationToken = default(CancellationToken));

        #endregion Outputs
    }
}