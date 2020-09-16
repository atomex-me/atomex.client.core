using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Atomex.Blockchain.Abstract;
using Atomex.Core;

namespace Atomex.Wallets.Abstract
{
    public interface IAccountDataRepository
    {
        #region Addresses

        Task<bool> UpsertAddressAsync(
            WalletAddress walletAddress,
            CancellationToken cancellationToken = default);

        Task<int> UpsertAddressesAsync(
            IEnumerable<WalletAddress> walletAddresses,
            CancellationToken cancellationToken = default);

        Task<bool> TryInsertAddressAsync(
            WalletAddress walletAddress,
            CancellationToken cancellationToken = default);

        Task<WalletAddress> GetWalletAddressAsync(
            string currency,
            string address,
            CancellationToken cancellationToken = default);

        Task<WalletAddress> GetLastActiveWalletAddressAsync(
            string currency,
            int chain,
            CancellationToken cancellationToken = default);

        Task<IEnumerable<WalletAddress>> GetAddressesAsync(
            string currency,
            int offset = 0,
            int limit = int.MaxValue,
            CancellationToken cancellationToken = default);

        Task<IEnumerable<WalletAddress>> GetUnspentAddressesAsync(
            string currency,
            CancellationToken cancellationToken = default);

        #endregion Addresses

        #region Transactions

        Task<bool> UpsertTransactionAsync<T>(
            T tx,
            CancellationToken cancellationToken = default)
            where T : IBlockchainTransaction;

        Task<int> UpsertTransactionsAsync<T>(
            IEnumerable<T> txs,
            CancellationToken cancellationToken = default)
            where T : IBlockchainTransaction;

        Task<T> GetTransactionByIdAsync<T>(
            string currency,
            string txId,
            CancellationToken cancellationToken = default)
            where T : IBlockchainTransaction;

        Task<IEnumerable<T>> GetTransactionsAsync<T>(
            string currency,
            int offset = 0,
            int limit = int.MaxValue,
            CancellationToken cancellationToken = default)
            where T : IBlockchainTransaction;

        Task<IEnumerable<T>> GetUnconfirmedTransactionsAsync<T>(
            string currency,
            int offset = 0,
            int limit = int.MaxValue,
            CancellationToken cancellationToken = default)
            where T : IBlockchainTransaction;

        Task<bool> RemoveTransactionByIdAsync(
            string currency,
            string txId,
            CancellationToken cancellationToken = default);

        #endregion Transactions

        #region Outputs

        Task<bool> UpsertOutputAsync(
            ITxOutput output,
            string currency,
            string address,
            CancellationToken cancellationToken = default);

        Task<int> UpsertOutputsAsync(
            IEnumerable<ITxOutput> outputs,
            string currency,
            string address,
            CancellationToken cancellationToken = default);

        Task<IEnumerable<TOutput>> GetAvailableOutputsAsync<TOutput, TTransaction>(
            string currency,
            CancellationToken cancellationToken = default)
            where TOutput : ITxOutput
            where TTransaction : IBlockchainTransaction;

        Task<IEnumerable<TOutput>> GetAvailableOutputsAsync<TOutput, TTransaction>(
            string currency,
            string address,
            CancellationToken cancellationToken = default)
            where TOutput : ITxOutput
            where TTransaction : IBlockchainTransaction;

        Task<IEnumerable<T>> GetOutputsAsync<T>(
            string currency,
            CancellationToken cancellationToken = default)
            where T : ITxOutput;

        Task<IEnumerable<T>> GetOutputsAsync<T>(
            string currency,
            string address,
            CancellationToken cancellationToken = default)
            where T : ITxOutput;

        Task<T> GetOutputAsync<T>(
            string currency,
            string txId,
            uint index,
            CancellationToken cancellationToken = default)
            where T : ITxOutput;

        #endregion

        #region Orders

        Task<bool> UpsertOrderAsync(
            Order order,
            CancellationToken cancellationToken = default);

        Task<Order> GetOrderByIdAsync(
            string clientOrderId,
            CancellationToken cancellationToken = default);

        Task<Order> GetOrderByIdAsync(
            long id,
            CancellationToken cancellationToken = default);

        #endregion Orders

        #region Swaps

        Task<bool> InsertSwapAsync(
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

        #endregion Swaps
    }
}