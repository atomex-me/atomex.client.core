using System;
using System.Collections.Generic;
using System.Numerics;
using System.Security;
using System.Threading;
using System.Threading.Tasks;

using Atomex.Blockchain;
using Atomex.Blockchain.Abstract;
using Atomex.Blockchain.Bitcoin;
using Atomex.Blockchain.Tezos;
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

        Task<WalletAddress> GetWalletAddressAsync(
            string currency,
            string address,
            CancellationToken cancellationToken = default);

        Task<WalletAddress> GetLastActiveWalletAddressAsync(
            string currency,
            string keyPathPattern,
            int keyType,
            CancellationToken cancellationToken = default);

        Task<IEnumerable<WalletAddress>> GetUnspentAddressesAsync(
            string currency,
            bool includeUnconfirmed = true,
            CancellationToken cancellationToken = default);

        Task<IEnumerable<WalletAddress>> GetAddressesAsync(
            string currency,
            CancellationToken cancellationToken = default);

        #endregion Addresses

        #region TezosTokens

        Task<WalletAddress> GetTokenAddressAsync(
            string currency,
            string tokenContract,
            BigInteger tokenId,
            string address,
            CancellationToken cancellationToken = default);

        Task<IEnumerable<WalletAddress>> GetUnspentTokenAddressesAsync(
            string currency,
            string tokenContract,
            decimal tokenId,
            CancellationToken cancellationToken = default);

        Task<IEnumerable<WalletAddress>> GetTokenAddressesAsync(
            CancellationToken cancellationToken = default);

        Task<IEnumerable<WalletAddress>> GetTokenAddressesAsync(
            string address,
            string tokenContract,
            CancellationToken cancellationToken = default);

        Task<IEnumerable<WalletAddress>> GetTokenAddressesByContractAsync(
            string tokenContract,
            CancellationToken cancellationToken = default);

        Task<int> UpsertTokenAddressesAsync(
            IEnumerable<WalletAddress> walletAddresses,
            CancellationToken cancellationToken = default);

        Task<int> UpsertTokenTransfersAsync(
            IEnumerable<TezosTokenTransfer> tokenTransfers,
            CancellationToken cancellationToken = default);

        Task<IEnumerable<TezosTokenTransfer>> GetTokenTransfersAsync(
            string contractAddress,
            int offset = 0,
            int limit = int.MaxValue,
            SortDirection sort = SortDirection.Desc,
            CancellationToken cancellationToken = default);

        Task<IEnumerable<(TezosTokenTransfer Transfer, TransactionMetadata Metadata)>> GetTokenTransfersWithMetadataAsync(
            string contractAddress,
            int offset = 0,
            int limit = int.MaxValue,
            SortDirection sort = SortDirection.Desc,
            CancellationToken cancellationToken = default);

        Task<int> UpsertTokenContractsAsync(
            IEnumerable<TokenContract> tokenContracts,
            CancellationToken cancellationToken = default);

        Task<IEnumerable<TokenContract>> GetTokenContractsAsync(
            CancellationToken cancellationToken = default);

        #endregion TezosTokens

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

        Task<(T,M)> GetTransactionWithMetadataByIdAsync<T,M>(
            string currency,
            string txId,
            CancellationToken cancellationToken = default)
            where T : ITransaction
            where M : ITransactionMetadata;

        Task<(ITransaction, ITransactionMetadata)> GetTransactionWithMetadataByIdAsync(
            string currency,
            string txId,
            Type transactionType,
            Type metadataType,
            CancellationToken cancellationToken = default);

        Task<IEnumerable<T>> GetTransactionsAsync<T>(
            string currency,
            int offset = 0,
            int limit = int.MaxValue,
            SortDirection sort = SortDirection.Desc,
            CancellationToken cancellationToken = default)
            where T : ITransaction;

        Task<IEnumerable<ITransaction>> GetTransactionsAsync(
            string currency,
            Type transactionType,
            int offset = 0,
            int limit = int.MaxValue,
            SortDirection sort = SortDirection.Desc,
            CancellationToken cancellationToken = default);

        Task<IEnumerable<(ITransaction, ITransactionMetadata)>> GetTransactionsWithMetadataAsync(
            string currency,
            Type transactionType,
            Type metadataType,
            int offset = 0,
            int limit = int.MaxValue,
            SortDirection sort = SortDirection.Desc,
            CancellationToken cancellationToken = default);

        Task<IEnumerable<(T, M)>> GetTransactionsWithMetadataAsync<T, M>(
            string currency,
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