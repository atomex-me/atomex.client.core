using System;
using System.Collections.Generic;
using System.Security;
using System.Threading;
using System.Threading.Tasks;

using Atomex.Blockchain.Abstract;
using Atomex.Blockchain.BitcoinBased;
using Atomex.Blockchain.Tezos;
using Atomex.Core;

namespace Atomex.Wallet.Abstract
{
    public interface ILocalStorage
    {
        public event EventHandler<BalanceChangedEventArgs> BalanceChanged;
        public event EventHandler<TransactionsChangedEventArgs> TransactionsChanged;

        void ChangePassword(SecureString newPassword);

        #region Addresses

        Task<bool> UpsertAddressAsync(WalletAddress walletAddress);

        Task<int> UpsertAddressesAsync(
            IEnumerable<WalletAddress> walletAddresses);

        Task<WalletAddress> GetWalletAddressAsync(
            string currency,
            string address);

        Task<WalletAddress> GetLastActiveWalletAddressAsync(
            string currency,
            uint chain,
            int keyType);

        Task<WalletAddress> GetLastActiveWalletAddressByAccountAsync(
            string currency,
            int keyType);

        Task<IEnumerable<WalletAddress>> GetUnspentAddressesAsync(
            string currency,
            bool includeUnconfirmed = true);

        Task<IEnumerable<WalletAddress>> GetAddressesAsync(string currency);

        #endregion Addresses

        #region TezosTokens

        Task<WalletAddress> GetTokenAddressAsync(
            string currency,
            string tokenContract,
            decimal tokenId,
            string address);

        Task<IEnumerable<WalletAddress>> GetUnspentTokenAddressesAsync(
            string currency,
            string tokenContract,
            decimal tokenId);

        Task<IEnumerable<WalletAddress>> GetTokenAddressesAsync();

        Task<IEnumerable<WalletAddress>> GetTokenAddressesAsync(
            string address,
            string tokenContract);

        Task<IEnumerable<WalletAddress>> GetTokenAddressesByContractAsync(
            string tokenContract);

        Task<int> UpsertTokenAddressesAsync(
            IEnumerable<WalletAddress> walletAddresses);

        Task<int> UpsertTokenTransfersAsync(
            IEnumerable<TokenTransfer> tokenTransfers);

        Task<IEnumerable<TokenTransfer>> GetTokenTransfersAsync(
            string contractAddress,
            int offset = 0,
            int limit = 20);

        Task<int> UpsertTokenContractsAsync(
            IEnumerable<TokenContract> tokenContracts);

        Task<IEnumerable<TokenContract>> GetTokenContractsAsync();

        #endregion TezosTokens

        #region Transactions

        Task<bool> UpsertTransactionAsync(
            IBlockchainTransaction tx,
            bool notifyIfNewOrChanged = false,
            CancellationToken cancellationToken = default);

        Task<bool> UpsertTransactionsAsync(
            IEnumerable<IBlockchainTransaction> txs,
            bool notifyIfNewOrChanged = false,
            CancellationToken cancellationToken = default);

        Task<T> GetTransactionByIdAsync<T>(string currency, string txId)
            where T : IBlockchainTransaction;

        Task<IEnumerable<T>> GetTransactionsAsync<T>(string currency)
            where T : IBlockchainTransaction;

        Task<IEnumerable<IBlockchainTransaction>> GetTransactionsAsync(
            string currency,
            Type transactionType);

        Task<IEnumerable<T>> GetUnconfirmedTransactionsAsync<T>(string currency)
            where T : IBlockchainTransaction;

        Task<bool> RemoveTransactionByIdAsync(string id);

        #endregion Transactions

        #region Outputs

        Task<bool> UpsertOutputsAsync(
            IEnumerable<BitcoinBasedTxOutput> outputs,
            string currency,
            NBitcoin.Network network);

        Task<IEnumerable<BitcoinBasedTxOutput>> GetAvailableOutputsAsync(
            string currency);

        Task<IEnumerable<BitcoinBasedTxOutput>> GetAvailableOutputsAsync(
            string currency,
            string address);

        Task<IEnumerable<BitcoinBasedTxOutput>> GetOutputsAsync(
            string currency);

        Task<IEnumerable<BitcoinBasedTxOutput>> GetOutputsAsync(
            string currency,
            string address);

        #endregion Outputs

        #region Orders

        Task<bool> UpsertOrderAsync(Order order);
        Task<bool> RemoveAllOrdersAsync();
        Order GetOrderById(string clientOrderId);
        Order GetOrderById(long id);
        Task<bool> RemoveOrderByIdAsync(long id);

        #endregion Orders

        #region Swaps

        Task<bool> AddSwapAsync(Swap swap);
        Task<bool> UpdateSwapAsync(Swap swap);
        Task<Swap> GetSwapByIdAsync(long id);
        Task<IEnumerable<Swap>> GetSwapsAsync();

        #endregion Swaps
    }
}