using System;
using System.Collections.Generic;
using System.Security;
using System.Threading.Tasks;

using Atomex.Blockchain.Abstract;
using Atomex.Blockchain.Tezos;
using Atomex.Core;

namespace Atomex.Wallet.Abstract
{
    public interface IAccountDataRepository_OLD
    {
        void ChangePassword(SecureString newPassword);

        #region Addresses

        Task<bool> UpsertAddressAsync(
            WalletAddress_OLD walletAddress);

        Task<int> UpsertAddressesAsync(
            IEnumerable<WalletAddress_OLD> walletAddresses);

        Task<bool> TryInsertAddressAsync(
            WalletAddress_OLD walletAddress);

        Task<WalletAddress_OLD> GetWalletAddressAsync(
            string currency,
            string address);

        Task<WalletAddress_OLD> GetLastActiveWalletAddressAsync(
            string currency,
            uint chain,
            int keyType);

        Task<WalletAddress_OLD> GetLastActiveWalletAddressByAccountAsync(
            string currency,
            int keyType);

        Task<IEnumerable<WalletAddress_OLD>> GetUnspentAddressesAsync(
            string currency,
            bool includeUnconfirmed = true);

        Task<IEnumerable<WalletAddress_OLD>> GetAddressesAsync(
            string currency);

        Task<bool> RemoveAddressAsync(
            string currency,
            string address);

        #endregion Addresses

        #region TezosTokens

        Task<WalletAddress_OLD> GetTezosTokenAddressAsync(
            string currency,
            string tokenContract,
            decimal tokenId,
            string address);

        Task<IEnumerable<WalletAddress_OLD>> GetUnspentTezosTokenAddressesAsync(
            string currency,
            string tokenContract,
            decimal tokenId);

        Task<IEnumerable<WalletAddress_OLD>> GetTezosTokenAddressesAsync();

        Task<IEnumerable<WalletAddress_OLD>> GetTezosTokenAddressesAsync(
            string address);

        Task<IEnumerable<WalletAddress_OLD>> GetTezosTokenAddressesAsync(
            string address,
            string tokenContract);

        Task<IEnumerable<WalletAddress_OLD>> GetTezosTokenAddressesByContractAsync(
            string tokenContract);

        Task<bool> TryInsertTezosTokenAddressAsync(
            WalletAddress_OLD address);

        Task<int> UpsertTezosTokenAddressesAsync(
            IEnumerable<WalletAddress_OLD> walletAddresses);

        Task<int> UpsertTezosTokenTransfersAsync(
            IEnumerable<TokenTransfer> tokenTransfers);

        Task<IEnumerable<TokenTransfer>> GetTezosTokenTransfersAsync(
            string contractAddress,
            int offset = 0,
            int limit = 20);

        Task<int> UpsertTezosTokenContractsAsync(
            IEnumerable<TokenContract> tokenContracts);

        Task<IEnumerable<TokenContract>> GetTezosTokenContractsAsync();

        #endregion TezosTokens

        #region Transactions

        Task<bool> UpsertTransactionAsync(
            IBlockchainTransaction_OLD tx);

        Task<IBlockchainTransaction_OLD> GetTransactionByIdAsync(
            string currency,
            string txId,
            Type transactionType);

        Task<IEnumerable<IBlockchainTransaction_OLD>> GetTransactionsAsync(
            string currency,
            Type transactionType);

        Task<IEnumerable<IBlockchainTransaction_OLD>> GetUnconfirmedTransactionsAsync(
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