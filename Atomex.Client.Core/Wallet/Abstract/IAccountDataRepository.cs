using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using Atomex.Blockchain.Abstract;
using Atomex.Blockchain.Tezos;
using Atomex.Core;

namespace Atomex.Wallet.Abstract
{
    public interface IAccountDataRepository
    {
        #region Addresses

        Task<bool> UpsertAddressAsync(
            WalletAddress walletAddress);

        Task<int> UpsertAddressesAsync(
            IEnumerable<WalletAddress> walletAddresses);

        Task<bool> TryInsertAddressAsync(
            WalletAddress walletAddress);

        Task<WalletAddress> GetWalletAddressAsync(
            string currency,
            string address);

        Task<WalletAddress> GetLastActiveWalletAddressAsync(
            string currency,
            int chain);

        Task<IEnumerable<WalletAddress>> GetUnspentAddressesAsync(
            string currency,
            bool includeUnconfirmed = true);

        Task<IEnumerable<WalletAddress>> GetAddressesAsync(
            string currency);

        Task<WalletAddress> GetTezosTokenAddressAsync(
            string uniqueTokenId,
            string address);

        Task<IEnumerable<WalletAddress>> GetTezosTokenAddressesAsync();

        Task<IEnumerable<WalletAddress>> GetTezosTokenAddressesAsync(
            string address);

        Task<IEnumerable<WalletAddress>> GetTezosTokenAddressesAsync(
            string address,
            string contractAddress);

        Task<int> UpsertTezosTokenAddressesAsync(
            IEnumerable<WalletAddress> walletAddresses);

        #endregion Addresses

        #region TokenTransfers

        Task<int> UpsertTezosTokenTransfersAsync(
            IEnumerable<TokenTransfer> tokenTransfers);

        #endregion TokenTransfers

        #region Transactions

        Task<bool> UpsertTransactionAsync(
            IBlockchainTransaction tx);

        Task<IBlockchainTransaction> GetTransactionByIdAsync(
            string currency,
            string txId,
            Type transactionType);

        Task<IEnumerable<IBlockchainTransaction>> GetTransactionsAsync(
            string currency,
            Type transactionType);

        Task<IEnumerable<IBlockchainTransaction>> GetUnconfirmedTransactionsAsync(
            string currency,
            Type transactionType);

        Task<bool> RemoveTransactionByIdAsync(
            string id);

        #endregion Transactions

        #region Outputs

        Task<bool> UpsertOutputsAsync(
            IEnumerable<ITxOutput> outputs,
            string currency,
            string address);
        Task<IEnumerable<ITxOutput>> GetAvailableOutputsAsync(
            string currency,
            Type outputType,
            Type transactionType);
        Task<IEnumerable<ITxOutput>> GetAvailableOutputsAsync(
            string currency,
            string address,
            Type outputType,
            Type transactionType);
        Task<IEnumerable<ITxOutput>> GetOutputsAsync(
            string currency,
            Type outputType);
        Task<IEnumerable<ITxOutput>> GetOutputsAsync(
            string currency,
            string address,
            Type outputType);
        Task<ITxOutput> GetOutputAsync(
            string currency,
            string txId,
            uint index,
            Type outputType);

        #endregion Outputs

        #region Orders

        Task<bool> UpsertOrderAsync(
            Order order);
        Order GetOrderById(
            string clientOrderId);
        Order GetOrderById(long id);

        #endregion Orders

        #region Swaps

        Task<bool> AddSwapAsync(Swap swap);
        Task<bool> UpdateSwapAsync(Swap swap);
        Task<Swap> GetSwapByIdAsync(long id);
        Task<IEnumerable<Swap>> GetSwapsAsync();

        #endregion Swaps
    }
}