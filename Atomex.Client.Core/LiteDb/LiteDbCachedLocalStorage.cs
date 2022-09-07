using System;
using System.Collections.Generic;
using System.Security;
using System.Threading;
using System.Threading.Tasks;

using Atomex.Abstract;
using Atomex.Blockchain.Abstract;
using Atomex.Blockchain.BitcoinBased;
using Atomex.Blockchain.Tezos;
using Atomex.Core;
using Atomex.Wallet.Abstract;
using Atomex.Wallet;

namespace Atomex.LiteDb
{
    public class LiteDbCachedLocalStorage : ILocalStorage
    {
        public event EventHandler<BalanceChangedEventArgs> BalanceChanged;
        public event EventHandler<TransactionsChangedEventArgs> TransactionsChanged;

        private readonly LiteDbLocalStorage _liteDbLocalStorage;

        public LiteDbCachedLocalStorage(
            string pathToDb,
            SecureString password,
            ICurrencies currencies,
            Network network,
            Action<MigrationActionType> migrationComplete = null)
        {
            _liteDbLocalStorage = new LiteDbLocalStorage(
                pathToDb,
                password,
                currencies,
                network,
                migrationComplete);
        }

        public void ChangePassword(SecureString newPassword)
        {
            _liteDbLocalStorage.ChangePassword(newPassword);
        }

        #region Addresses

        public Task<bool> UpsertAddressAsync(WalletAddress walletAddress)
        {
            // todo: balance updated

            return _liteDbLocalStorage.UpsertAddressAsync(walletAddress);
        }

        public Task<int> UpsertAddressesAsync(IEnumerable<WalletAddress> walletAddresses)
        {
            // todo: balance updated

            return _liteDbLocalStorage.UpsertAddressesAsync(walletAddresses);
        }

        public Task<WalletAddress> GetWalletAddressAsync(string currency, string address)
        {
            return _liteDbLocalStorage.GetWalletAddressAsync(currency, address);
        }

        public Task<WalletAddress> GetLastActiveWalletAddressAsync(
            string currency,
            uint chain,
            int keyType)
        {
            return _liteDbLocalStorage.GetLastActiveWalletAddressAsync(currency, chain, keyType);
        }

        public Task<WalletAddress> GetLastActiveWalletAddressByAccountAsync(
            string currency,
            int keyType)
        {
            return _liteDbLocalStorage.GetLastActiveWalletAddressByAccountAsync(currency, keyType);
        }

        public Task<IEnumerable<WalletAddress>> GetUnspentAddressesAsync(
            string currency,
            bool includeUnconfirmed = true)
        {
            return _liteDbLocalStorage.GetUnspentAddressesAsync(currency, includeUnconfirmed);
        }

        public Task<IEnumerable<WalletAddress>> GetAddressesAsync(string currency)
        {
            return _liteDbLocalStorage.GetAddressesAsync(currency);
        }

        #endregion Addresses

        #region TezosTokens

        public Task<WalletAddress> GetTezosTokenAddressAsync(
            string currency,
            string tokenContract,
            decimal tokenId,
            string address)
        {
            return _liteDbLocalStorage.GetTezosTokenAddressAsync(currency, tokenContract, tokenId, address);
        }

        public Task<IEnumerable<WalletAddress>> GetTezosTokenAddressesAsync()
        {
            return _liteDbLocalStorage.GetTezosTokenAddressesAsync();
        }

        public Task<IEnumerable<WalletAddress>> GetTezosTokenAddressesAsync(
            string address,
            string tokenContract)
        {
            return _liteDbLocalStorage.GetTezosTokenAddressesAsync(address, tokenContract);
        }

        public Task<IEnumerable<WalletAddress>> GetTezosTokenAddressesByContractAsync(
            string tokenContract)
        {
            return _liteDbLocalStorage.GetTezosTokenAddressesByContractAsync(tokenContract);
        }

        public Task<int> UpsertTezosTokenAddressesAsync(IEnumerable<WalletAddress> walletAddresses)
        {
            return _liteDbLocalStorage.UpsertTezosTokenAddressesAsync(walletAddresses);
        }

        public Task<IEnumerable<WalletAddress>> GetUnspentTezosTokenAddressesAsync(
            string currency,
            string tokenContract,
            decimal tokenId)
        {
            return _liteDbLocalStorage.GetUnspentTezosTokenAddressesAsync(
                currency,
                tokenContract,
                tokenId);
        }

        public Task<int> UpsertTezosTokenTransfersAsync(
            IEnumerable<TokenTransfer> tokenTransfers)
        {
            return _liteDbLocalStorage.UpsertTezosTokenTransfersAsync(tokenTransfers);
        }

        public Task<IEnumerable<TokenTransfer>> GetTezosTokenTransfersAsync(
            string contractAddress,
            int offset = 0,
            int limit = int.MaxValue)
        {
            return _liteDbLocalStorage.GetTezosTokenTransfersAsync(contractAddress, offset, limit);
        }

        public Task<int> UpsertTezosTokenContractsAsync(
            IEnumerable<TokenContract> tokenContracts)
        {
            return _liteDbLocalStorage.UpsertTezosTokenContractsAsync(tokenContracts);
        }

        public Task<IEnumerable<TokenContract>> GetTezosTokenContractsAsync()
        {
            return _liteDbLocalStorage.GetTezosTokenContractsAsync();
        }

        #endregion TezosTokens

        #region Transactions

        public Task<bool> UpsertTransactionAsync(
            IBlockchainTransaction tx,
            bool notifyIfNewOrChanged = false,
            CancellationToken cancellationToken = default)
        {
            return _liteDbLocalStorage.UpsertTransactionAsync(tx, notifyIfNewOrChanged, cancellationToken);
        }

        public Task<bool> UpsertTransactionsAsync(
            IEnumerable<IBlockchainTransaction> txs,
            bool notifyIfNewOrChanged = false,
            CancellationToken cancellationToken = default)
        {
            return _liteDbLocalStorage.UpsertTransactionsAsync(txs, notifyIfNewOrChanged, cancellationToken);
        }

        public Task<T> GetTransactionByIdAsync<T>(
            string currency,
            string txId) where T : IBlockchainTransaction
        {
            return _liteDbLocalStorage.GetTransactionByIdAsync<T>(currency, txId);
        }

        public Task<IEnumerable<T>> GetTransactionsAsync<T>(string currency)
             where T : IBlockchainTransaction
        {
            return _liteDbLocalStorage.GetTransactionsAsync<T>(currency);
        }

        public Task<IEnumerable<T>> GetUnconfirmedTransactionsAsync<T>(
            string currency) where T : IBlockchainTransaction
        {
            return _liteDbLocalStorage.GetUnconfirmedTransactionsAsync<T>(currency);
        }

        public Task<bool> RemoveTransactionByIdAsync(string id)
        {
            return _liteDbLocalStorage.RemoveTransactionByIdAsync(id);
        }

        #endregion Transactions

        #region Outputs

        public Task<bool> UpsertOutputsAsync(
            IEnumerable<BitcoinBasedTxOutput> outputs,
            string currency,
            NBitcoin.Network network)
        {
            return _liteDbLocalStorage.UpsertOutputsAsync(outputs, currency, network);
        }

        public Task<IEnumerable<BitcoinBasedTxOutput>> GetAvailableOutputsAsync(string currency)
        {
            return _liteDbLocalStorage.GetAvailableOutputsAsync(currency);
        }

        public Task<IEnumerable<BitcoinBasedTxOutput>> GetAvailableOutputsAsync(
            string currency,
            string address)
        {
            return _liteDbLocalStorage.GetAvailableOutputsAsync(currency, address);
        }

        public Task<IEnumerable<BitcoinBasedTxOutput>> GetOutputsAsync(string currency)
        {
            return _liteDbLocalStorage.GetOutputsAsync(currency);
        }

        public Task<IEnumerable<BitcoinBasedTxOutput>> GetOutputsAsync(
            string currency,
            string address)
        {
            return _liteDbLocalStorage.GetOutputsAsync(currency, address);
        }

        #endregion Outputs

        #region Orders

        public Task<bool> UpsertOrderAsync(Order order)
        {
            return _liteDbLocalStorage.UpsertOrderAsync(order);
        }

        public Task<bool> RemoveAllOrdersAsync()
        {
            return _liteDbLocalStorage.RemoveAllOrdersAsync();
        }

        public Order GetOrderById(string clientOrderId)
        {
            return _liteDbLocalStorage.GetOrderById(clientOrderId);
        }

        public Order GetOrderById(long id)
        {
            return _liteDbLocalStorage.GetOrderById(id);
        }

        public Task<bool> RemoveOrderByIdAsync(long id)
        {
            return _liteDbLocalStorage.RemoveOrderByIdAsync(id);
        }

        #endregion Orders

        #region Swaps

        public Task<bool> AddSwapAsync(Swap swap)
        {
            return _liteDbLocalStorage.AddSwapAsync(swap);
        }

        public Task<bool> UpdateSwapAsync(Swap swap)
        {
            return _liteDbLocalStorage.UpdateSwapAsync(swap);
        }

        public Task<Swap> GetSwapByIdAsync(long id)
        {
            return _liteDbLocalStorage.GetSwapByIdAsync(id);
        }

        public Task<IEnumerable<Swap>> GetSwapsAsync()
        {
            return _liteDbLocalStorage.GetSwapsAsync();
        }

        #endregion Swaps
    }
}