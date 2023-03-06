using System;
using System.Collections.Generic;
using System.Numerics;
using System.Security;
using System.Threading;
using System.Threading.Tasks;

using Atomex.Blockchain.Abstract;
using Atomex.Blockchain.Bitcoin;
using Atomex.Blockchain.Tezos.Tzkt;
using Atomex.Common;
using Atomex.Core;

namespace Atomex.Wallet.Abstract
{
    public enum MigrationActionType
    {
        XtzTransactionsDeleted,
        XtzTokensDataDeleted
    }

    public interface ILocalStorage
    {
        public event EventHandler<BalanceChangedEventArgs> BalanceChanged;
        public event EventHandler<TransactionsChangedEventArgs> TransactionsChanged;

        void ChangePassword(SecureString newPassword);
        void Close();

        #region Addresses

        Task<bool> UpsertAddressAsync(
            WalletAddress walletAddress,
            CancellationToken cancellationToken = default);

        Task<int> UpsertAddressesAsync(
            IEnumerable<WalletAddress> walletAddresses,
            CancellationToken cancellationToken = default);

        Task<WalletAddress> GetAddressAsync(
            string currency,
            string address,
            string tokenContract = null,
            BigInteger? tokenId = null,
            CancellationToken cancellationToken = default);

        Task<WalletAddress> GetLastActiveWalletAddressAsync(
            string currency,
            string keyPathPattern,
            int keyType,
            CancellationToken cancellationToken = default);

        Task<IEnumerable<WalletAddress>> GetUnspentAddressesAsync(
            string currency,
            string tokenContract = null,
            BigInteger? tokenId = null,
            bool includeUnconfirmed = true,
            CancellationToken cancellationToken = default);

        Task<IEnumerable<WalletAddress>> GetAddressesAsync(
            string currency,
            string tokenContract = null,
            string address = null,
            CancellationToken cancellationToken = default);

        #endregion Addresses

        #region Tokens

        Task<int> UpsertTokenContractsAsync(
            IEnumerable<TokenContract> tokenContracts,
            CancellationToken cancellationToken = default);

        Task<IEnumerable<TokenContract>> GetTokenContractsAsync(
            CancellationToken cancellationToken = default);

        #endregion Tokens

        #region Transactions

        Task<bool> UpsertTransactionAsync(
            ITransaction tx,
            bool notifyIfNewOrChanged = false,
            CancellationToken cancellationToken = default);

        Task<bool> UpsertTransactionsAsync(
            IEnumerable<ITransaction> txs,
            bool notifyIfNewOrChanged = false,
            CancellationToken cancellationToken = default);

        Task<T> GetTransactionByIdAsync<T>(
            string currency,
            string txId,
            CancellationToken cancellationToken = default)
            where T : ITransaction;

        Task<ITransaction> GetTransactionByIdAsync(
            string currency,
            string txId,
            Type transactionType,
            CancellationToken cancellationToken = default);

        Task<TransactionInfo<T, M>> GetTransactionWithMetadataByIdAsync<T,M>(
            string currency,
            string txId,
            CancellationToken cancellationToken = default)
            where T : ITransaction
            where M : ITransactionMetadata;

        Task<TransactionInfo<ITransaction, ITransactionMetadata>> GetTransactionWithMetadataByIdAsync(
            string currency,
            string txId,
            Type transactionType,
            Type metadataType,
            CancellationToken cancellationToken = default);

        Task<IEnumerable<T>> GetTransactionsAsync<T>(
            string currency,
            string tokenContract = null,
            int offset = 0,
            int limit = int.MaxValue,
            SortDirection sort = SortDirection.Desc,
            CancellationToken cancellationToken = default)
            where T : ITransaction;

        Task<IEnumerable<ITransaction>> GetTransactionsAsync(
            string currency,
            Type transactionType,
            string tokenContract = null,
            int offset = 0,
            int limit = int.MaxValue,
            SortDirection sort = SortDirection.Desc,
            CancellationToken cancellationToken = default);

        Task<IEnumerable<TransactionInfo<ITransaction, ITransactionMetadata>>> GetTransactionsWithMetadataAsync(
            string currency,
            Type transactionType,
            Type metadataType,
            string tokenContract = null,
            int offset = 0,
            int limit = int.MaxValue,
            SortDirection sort = SortDirection.Desc,
            CancellationToken cancellationToken = default);

        Task<IEnumerable<TransactionInfo<T, M>>> GetTransactionsWithMetadataAsync<T, M>(
            string currency,
            string tokenContract = null,
            int offset = 0,
            int limit = int.MaxValue,
            SortDirection sort = SortDirection.Desc,
            CancellationToken cancellationToken = default)
            where T : ITransaction
            where M : ITransactionMetadata;

        Task<IEnumerable<T>> GetUnconfirmedTransactionsAsync<T>(
            string currency,
            CancellationToken cancellationToken = default)
            where T : ITransaction;

        Task<bool> RemoveTransactionByIdAsync(
            string id,
            string currency,
            CancellationToken cancellationToken = default);

        Task<bool> UpsertTransactionsMetadataAsync(
            IEnumerable<ITransactionMetadata> metadata,
            bool notifyIfNewOrChanged = false,
            CancellationToken cancellationToken = default);

        Task<T> GetTransactionMetadataByIdAsync<T>(
            string currency,
            string txId,
            CancellationToken cancellationToken = default)
            where T : ITransactionMetadata;

        Task<ITransactionMetadata> GetTransactionMetadataByIdAsync(
            string currency,
            string txId,
            Type type,
            CancellationToken cancellationToken = default);

        Task<int> RemoveTransactionsMetadataAsync(
            string currency,
            CancellationToken cancellationToken = default);

        #endregion Transactions

        #region Outputs

        Task<bool> UpsertOutputsAsync(
            IEnumerable<BitcoinTxOutput> outputs,
            string currency,
            NBitcoin.Network network,
            CancellationToken cancellationToken = default);

        Task<IEnumerable<BitcoinTxOutput>> GetAvailableOutputsAsync(
            string currency,
            CancellationToken cancellationToken = default);

        Task<IEnumerable<BitcoinTxOutput>> GetAvailableOutputsAsync(
            string currency,
            string address,
            CancellationToken cancellationToken = default);

        Task<IEnumerable<BitcoinTxOutput>> GetOutputsAsync(
            string currency,
            CancellationToken cancellationToken = default);

        Task<IEnumerable<BitcoinTxOutput>> GetOutputsAsync(
            string currency,
            string address,
            CancellationToken cancellationToken = default);

        Task<BitcoinTxOutput> GetOutputAsync(
            string currency,
            string txId,
            uint index,
            CancellationToken cancellationToken = default);

        #endregion Outputs

        #region Orders

        Task<bool> UpsertOrderAsync(
            Order order,
            CancellationToken cancellationToken = default);

        Task<bool> RemoveAllOrdersAsync(
            CancellationToken cancellationToken = default);

        Order GetOrderById(
            string clientOrderId,
            CancellationToken cancellationToken = default);

        Order GetOrderById(
            long id,
            CancellationToken cancellationToken = default);

        Task<bool> RemoveOrderByIdAsync(
            long id,
            CancellationToken cancellationToken = default);

        #endregion Orders

        #region Swaps

        Task<bool> AddSwapAsync(
            Swap swap,
            CancellationToken cancellationToken = default);

        Task<bool> UpdateSwapAsync(
            Swap swap,
            CancellationToken cancellationToken = default);

        Task<Swap> GetSwapByIdAsync(
            long id,
            CancellationToken cancellationToken = default);

        Task<IEnumerable<Swap>> GetSwapsAsync(
            int offset = 0,
            int limit = int.MaxValue,
            CancellationToken cancellationToken = default);

        Task<Swap> GetSwapByPaymentTxIdAsync(
            string txId,
            CancellationToken cancellationToken = default);

        #endregion Swaps
    }
}