using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Security;
using System.Threading;
using System.Threading.Tasks;

using Atomex.Blockchain.Abstract;
using Atomex.Blockchain.Bitcoin;
using Atomex.Blockchain.Tezos;
using Atomex.Blockchain.Tezos.Tzkt;
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

        //private IDictionary<string, WalletAddress> _walletByAddress;
        //private IDictionary<string, List<WalletAddress>> _walletsByCurrency;
        //private readonly Lazy<IDictionary<long, Swap>> _swapById;

        public LiteDbCachedLocalStorage(
            string pathToDb,
            SecureString password)
        {
            _liteDbLocalStorage = new LiteDbLocalStorage(
                pathToDb,
                password);

            //_swapById = new Lazy<IDictionary<long, Swap>>(
            //    valueFactory: () => new ConcurrentDictionary<long, Swap>(),
            //    isThreadSafe: true);
        }

        public void ChangePassword(SecureString newPassword)
        {
            _liteDbLocalStorage.ChangePassword(newPassword);
        }

        #region Addresses

        public async Task<bool> UpsertAddressAsync(
            WalletAddress walletAddress,
            CancellationToken cancellationToken = default)
        {
            var localAddress = await _liteDbLocalStorage
                .GetWalletAddressAsync(
                    walletAddress.Currency,
                    walletAddress.Address,
                    cancellationToken)
                .ConfigureAwait(false);

            var balanceChanged = localAddress == null || localAddress.Balance != walletAddress.Balance;

            var upsertResult = await _liteDbLocalStorage
                .UpsertAddressAsync(walletAddress, cancellationToken)
                .ConfigureAwait(false);

            if (balanceChanged)
                BalanceChanged?.Invoke(this, new BalanceChangedEventArgs {
                    Currencies = new string[] { walletAddress.Currency },
                    Addresses = new string[] { walletAddress.Address }
                });

            return upsertResult;
        }

        public async Task<int> UpsertAddressesAsync(
            IEnumerable<WalletAddress> walletAddresses,
            CancellationToken cancellationToken = default)
        {
            var changedCurrencies = new HashSet<string>();
            var changedAddresses = new HashSet<string>();
            
            foreach (var walletAddress in walletAddresses)
            {
                var localAddress = await _liteDbLocalStorage
                    .GetWalletAddressAsync(
                        walletAddress.Currency,
                        walletAddress.Address,
                        cancellationToken)
                    .ConfigureAwait(false);

                var balanceChanged = localAddress == null || localAddress.Balance != walletAddress.Balance;

                if (balanceChanged)
                {
                    changedCurrencies.Add(walletAddress.Currency);
                    changedAddresses.Add(walletAddress.Address);
                }
            }

            var upsertResult = await _liteDbLocalStorage
                .UpsertAddressesAsync(walletAddresses)
                .ConfigureAwait(false);;

            if (changedCurrencies.Any())
                BalanceChanged?.Invoke(this, new BalanceChangedEventArgs
                {
                    Currencies = changedCurrencies.ToArray(),
                    Addresses = changedAddresses.ToArray()
                });

            return upsertResult;
        }

        public Task<WalletAddress> GetWalletAddressAsync(
            string currency,
            string address,
            CancellationToken cancellationToken = default)
        {
            return _liteDbLocalStorage.GetWalletAddressAsync(currency, address);
        }

        public Task<WalletAddress> GetLastActiveWalletAddressAsync(
            string currency,
            uint chain,
            int keyType,
            CancellationToken cancellationToken = default)
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

        public Task<WalletAddress> GetTokenAddressAsync(
            string currency,
            string tokenContract,
            BigInteger tokenId,
            string address)
        {
            return _liteDbLocalStorage.GetTokenAddressAsync(currency, tokenContract, tokenId, address);
        }

        public Task<IEnumerable<WalletAddress>> GetTokenAddressesAsync()
        {
            return _liteDbLocalStorage.GetTokenAddressesAsync();
        }

        public Task<IEnumerable<WalletAddress>> GetTokenAddressesAsync(
            string address,
            string tokenContract)
        {
            return _liteDbLocalStorage.GetTokenAddressesAsync(address, tokenContract);
        }

        public Task<IEnumerable<WalletAddress>> GetTokenAddressesByContractAsync(
            string tokenContract)
        {
            return _liteDbLocalStorage.GetTokenAddressesByContractAsync(tokenContract);
        }

        public async Task<int> UpsertTokenAddressesAsync(IEnumerable<WalletAddress> walletAddresses)
        {
            var changedTokens = new HashSet<(string, BigInteger)>();
            var changedAddresses = new HashSet<string>();

            foreach (var walletAddress in walletAddresses)
            {
                var localAddress = await _liteDbLocalStorage
                    .GetTokenAddressAsync(
                        walletAddress.Currency,
                        walletAddress.TokenBalance.Contract,
                        walletAddress.TokenBalance.TokenId,
                        walletAddress.Address)
                    .ConfigureAwait(false);

                var balanceChanged = localAddress == null || localAddress.TokenBalance.Balance != walletAddress.TokenBalance.Balance;

                if (balanceChanged)
                {
                    changedTokens.Add((walletAddress.TokenBalance.Contract, walletAddress.TokenBalance.TokenId));
                    changedAddresses.Add(walletAddress.Address);
                }
            }

            var upsertResult = await _liteDbLocalStorage
                .UpsertTokenAddressesAsync(walletAddresses)
                .ConfigureAwait(false); ;

            if (changedTokens.Any())
                BalanceChanged?.Invoke(this, new TokenBalanceChangedEventArgs
                {
                    Currencies = new string[] {},
                    Tokens = changedTokens.ToArray(),
                    Addresses = changedAddresses.ToArray()
                });

            return upsertResult;
        }

        public Task<IEnumerable<WalletAddress>> GetUnspentTokenAddressesAsync(
            string currency,
            string tokenContract,
            decimal tokenId)
        {
            return _liteDbLocalStorage.GetUnspentTokenAddressesAsync(
                currency,
                tokenContract,
                tokenId);
        }

        public async Task<int> UpsertTokenTransfersAsync(
            IEnumerable<TezosTokenTransfer> tokenTransfers)
        {
            var upsertResult = await _liteDbLocalStorage
                .UpsertTokenTransfersAsync(tokenTransfers)
                .ConfigureAwait(false);

            TransactionsChanged?.Invoke(this, new TransactionsChangedEventArgs(tokenTransfers));

            return upsertResult;
        }

        public Task<IEnumerable<TezosTokenTransfer>> GetTokenTransfersAsync(
            string contractAddress,
            int offset = 0,
            int limit = int.MaxValue)
        {
            return _liteDbLocalStorage.GetTokenTransfersAsync(contractAddress, offset, limit);
        }

        public Task<int> UpsertTokenContractsAsync(
            IEnumerable<TokenContract> tokenContracts)
        {
            return _liteDbLocalStorage.UpsertTokenContractsAsync(tokenContracts);
        }

        public Task<IEnumerable<TokenContract>> GetTokenContractsAsync()
        {
            return _liteDbLocalStorage.GetTokenContractsAsync();
        }

        #endregion TezosTokens

        #region Transactions

        public async Task<bool> UpsertTransactionAsync(
            ITransaction tx,
            bool notifyIfNewOrChanged = false,
            CancellationToken cancellationToken = default)
        {
            var upsertResult = await _liteDbLocalStorage
                .UpsertTransactionAsync(tx, notifyIfNewOrChanged, cancellationToken)
                .ConfigureAwait(false);

            TransactionsChanged?.Invoke(this, new TransactionsChangedEventArgs(tx));

            return upsertResult;
        }

        public async Task<bool> UpsertTransactionsAsync(
            IEnumerable<ITransaction> txs,
            bool notifyIfNewOrChanged = false,
            CancellationToken cancellationToken = default)
        {
            var upsertResult = await _liteDbLocalStorage
                .UpsertTransactionsAsync(txs, notifyIfNewOrChanged, cancellationToken)
                .ConfigureAwait(false);

            TransactionsChanged?.Invoke(this, new TransactionsChangedEventArgs(txs));

            return upsertResult;
        }

        public Task<T> GetTransactionByIdAsync<T>(
            string currency,
            string txId,
            CancellationToken cancellationToken = default) where T : ITransaction
        {
            return _liteDbLocalStorage.GetTransactionByIdAsync<T>(
                currency,
                txId,
                cancellationToken);
        }

        public Task<ITransaction> GetTransactionByIdAsync(
            string currency,
            string txId,
            Type transactionType,
            CancellationToken cancellationToken = default)
        {
            return _liteDbLocalStorage.GetTransactionByIdAsync(
                currency,
                txId,
                transactionType,
                cancellationToken);
        }

        public Task<IEnumerable<T>> GetTransactionsAsync<T>(
            string currency,
            CancellationToken cancellationToken = default)
             where T : ITransaction
        {
            return _liteDbLocalStorage.GetTransactionsAsync<T>(
                currency,
                cancellationToken);
        }

        public Task<IEnumerable<ITransaction>> GetTransactionsAsync(
            string currency,
            Type transactionType,
            CancellationToken cancellationToken = default)
        {
            return _liteDbLocalStorage.GetTransactionsAsync(
                currency,
                transactionType,
                cancellationToken);
        }

        public Task<IEnumerable<T>> GetUnconfirmedTransactionsAsync<T>(
            string currency,
            CancellationToken cancellationToken = default) where T : ITransaction
        {
            return _liteDbLocalStorage.GetUnconfirmedTransactionsAsync<T>(
                currency,
                cancellationToken);
        }

        public Task<bool> RemoveTransactionByIdAsync(
            string id,
            CancellationToken cancellationToken = default)
        {
            return _liteDbLocalStorage.RemoveTransactionByIdAsync(
                id,
                cancellationToken);
        }

        public Task<bool> UpsertTransactionsMetadataAsync(
            IEnumerable<ITransactionMetadata> metadata,
            bool notifyIfNewOrChanged = false,
            CancellationToken cancellationToken = default)
        {
            return _liteDbLocalStorage.UpsertTransactionsMetadataAsync(
                metadata,
                notifyIfNewOrChanged,
                cancellationToken);
        }

        public Task<T> GetTransactionMetadataByIdAsync<T>(
            string currency,
            string txId,
            CancellationToken cancellationToken = default) where T : ITransactionMetadata
        {
            return _liteDbLocalStorage.GetTransactionMetadataByIdAsync<T>(
                currency,
                txId,
                cancellationToken);
        }

        public Task<ITransactionMetadata> GetTransactionMetadataByIdAsync(
            string currency,
            string txId,
            Type type, 
            CancellationToken cancellationToken = default)
        {
            return _liteDbLocalStorage.GetTransactionMetadataByIdAsync(
                currency,
                txId,
                type,
                cancellationToken);
        }

        #endregion Transactions

        #region Outputs

        public Task<bool> UpsertOutputsAsync(
            IEnumerable<BitcoinTxOutput> outputs,
            string currency,
            NBitcoin.Network network,
            CancellationToken cancellationToken = default)
        {
            return _liteDbLocalStorage.UpsertOutputsAsync(
                outputs,
                currency,
                network,
                cancellationToken);
        }

        public Task<IEnumerable<BitcoinTxOutput>> GetAvailableOutputsAsync(
            string currency,
            CancellationToken cancellationToken = default)
        {
            return _liteDbLocalStorage.GetAvailableOutputsAsync(
                currency,
                cancellationToken);
        }

        public Task<IEnumerable<BitcoinTxOutput>> GetAvailableOutputsAsync(
            string currency,
            string address,
            CancellationToken cancellationToken = default)
        {
            return _liteDbLocalStorage.GetAvailableOutputsAsync(
                currency,
                address,
                cancellationToken);
        }

        public Task<IEnumerable<BitcoinTxOutput>> GetOutputsAsync(
            string currency,
            CancellationToken cancellationToken = default)
        {
            return _liteDbLocalStorage.GetOutputsAsync(
                currency,
                cancellationToken);
        }

        public Task<IEnumerable<BitcoinTxOutput>> GetOutputsAsync(
            string currency,
            string address,
            CancellationToken cancellationToken = default)
        {
            return _liteDbLocalStorage.GetOutputsAsync(
                currency, 
                address,
                cancellationToken);
        }

        public Task<BitcoinTxOutput> GetOutputAsync(
            string currency,
            string txId, uint index,
            CancellationToken cancellationToken = default)
        {
            return _liteDbLocalStorage.GetOutputAsync(
                currency,
                txId,
                index,
                cancellationToken);
        }

        #endregion Outputs

        #region Orders

        public Task<bool> UpsertOrderAsync(
            Order order,
            CancellationToken cancellationToken = default)
        {
            return _liteDbLocalStorage.UpsertOrderAsync(order);
        }

        public Task<bool> RemoveAllOrdersAsync(
            CancellationToken cancellationToken = default)
        {
            return _liteDbLocalStorage.RemoveAllOrdersAsync();
        }

        public Order GetOrderById(
            string clientOrderId,
            CancellationToken cancellationToken = default)
        {
            return _liteDbLocalStorage.GetOrderById(clientOrderId);
        }

        public Order GetOrderById(
            long id,
            CancellationToken cancellationToken = default)
        {
            return _liteDbLocalStorage.GetOrderById(id);
        }

        public Task<bool> RemoveOrderByIdAsync(
            long id,
            CancellationToken cancellationToken = default)
        {
            return _liteDbLocalStorage.RemoveOrderByIdAsync(id);
        }

        #endregion Orders

        #region Swaps

        public Task<bool> AddSwapAsync(
            Swap swap,
            CancellationToken cancellationToken = default)
        {
            //_swapById.Value.TryAdd(swap.Id, swap);

            return _liteDbLocalStorage.AddSwapAsync(swap);
        }

        public Task<bool> UpdateSwapAsync(
            Swap swap,
            CancellationToken cancellationToken = default)
        {
            //_swapById.Value[swap.Id] = swap;

            return _liteDbLocalStorage.UpdateSwapAsync(swap);
        }

        public async Task<Swap> GetSwapByIdAsync(
            long id,
            CancellationToken cancellationToken = default)
        {
            //if (_swapById.Value.TryGetValue(id, out var swap))
            //    return swap;

            return await _liteDbLocalStorage
                .GetSwapByIdAsync(id)
                .ConfigureAwait(false);
        }

        public Task<IEnumerable<Swap>> GetSwapsAsync(
            int offset = 0,
            int limit = int.MaxValue,
            CancellationToken cancellationToken = default)
        {
            return _liteDbLocalStorage.GetSwapsAsync();
        }

        public Task<Swap> GetSwapByPaymentTxIdAsync(
            string txId,
            CancellationToken cancellationToken = default)
        {
            return _liteDbLocalStorage.GetSwapByPaymentTxIdAsync(txId, cancellationToken);
        }

        #endregion Swaps
    }
}