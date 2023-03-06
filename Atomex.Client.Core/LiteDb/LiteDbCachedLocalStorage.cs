using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Security;
using System.Threading;
using System.Threading.Tasks;

using Atomex.Blockchain.Abstract;
using Atomex.Blockchain.Bitcoin;
using Atomex.Blockchain.Tezos.Tzkt;
using Atomex.Common;
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
            SecureString password,
            Network network)
        {
            _liteDbLocalStorage = new LiteDbLocalStorage(
                pathToDb,
                password,
                network);

            //_swapById = new Lazy<IDictionary<long, Swap>>(
            //    valueFactory: () => new ConcurrentDictionary<long, Swap>(),
            //    isThreadSafe: true);
        }

        public void ChangePassword(SecureString newPassword)
        {
            _liteDbLocalStorage.ChangePassword(newPassword);
        }

        public void Close()
        {
            _liteDbLocalStorage.Close();
        }

        #region Addresses

        public async Task<bool> UpsertAddressAsync(
            WalletAddress walletAddress,
            CancellationToken cancellationToken = default)
        {
            var localAddress = await _liteDbLocalStorage
                .GetAddressAsync(
                    currency: walletAddress.Currency,
                    address: walletAddress.Address,
                    tokenContract: walletAddress.TokenBalance?.Contract,
                    tokenId: walletAddress.TokenBalance?.TokenId,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            var balanceChanged = localAddress == null || localAddress.Balance != walletAddress.Balance;

            var upsertResult = await _liteDbLocalStorage
                .UpsertAddressAsync(walletAddress, cancellationToken)
                .ConfigureAwait(false);

            if (balanceChanged)
            {
                if (_liteDbLocalStorage.IsToken(walletAddress.Currency))
                {
                    BalanceChanged?.Invoke(this, new TokenBalanceChangedEventArgs
                    {
                        Currency  = walletAddress.Currency,
                        Addresses = new string[] { walletAddress.Address },
                        Tokens    = new (string, BigInteger)[] { (walletAddress.TokenBalance?.Contract, walletAddress.TokenBalance.TokenId) }
                    });
                }
                else
                {
                    BalanceChanged?.Invoke(this, new BalanceChangedEventArgs
                    {
                        Currency  = walletAddress.Currency,
                        Addresses = new string[] { walletAddress.Address }
                    });
                }
            }

            return upsertResult;
        }

        public async Task<int> UpsertAddressesAsync(
            IEnumerable<WalletAddress> walletAddresses,
            CancellationToken cancellationToken = default)
        {
            var upserted = 0;

            foreach (var walletAddressesGroup in walletAddresses.GroupBy(w => w.Currency))
            {
                var changedAddresses = new HashSet<string>();
                var changedTokens = new HashSet<(string, BigInteger)>();

                foreach (var walletAddress in walletAddressesGroup)
                {
                    var localAddress = await _liteDbLocalStorage
                        .GetAddressAsync(
                            currency: walletAddress.Currency,
                            address: walletAddress.Address,
                            tokenContract: walletAddress.TokenBalance?.Contract,
                            tokenId: walletAddress.TokenBalance?.TokenId,
                            cancellationToken: cancellationToken)
                        .ConfigureAwait(false);

                    var balanceChanged = localAddress == null || localAddress.Balance != walletAddress.Balance;

                    if (balanceChanged)
                    {
                        changedAddresses.Add(walletAddress.Address);

                        if (walletAddress.TokenBalance != null)
                            changedTokens.Add((walletAddress.TokenBalance.Contract, walletAddress.TokenBalance.TokenId));
                    }
                }

                upserted += await _liteDbLocalStorage
                    .UpsertAddressesAsync(walletAddressesGroup, cancellationToken)
                    .ConfigureAwait(false);

                if (changedAddresses.Any())
                {
                    if (_liteDbLocalStorage.IsToken(walletAddressesGroup.Key))
                    {
                        BalanceChanged?.Invoke(this, new TokenBalanceChangedEventArgs
                        {
                            Currency = walletAddressesGroup.Key,
                            Addresses = changedAddresses.ToArray(),
                            Tokens = changedTokens.ToArray()
                        });
                    }
                    else
                    {
                        BalanceChanged?.Invoke(this, new BalanceChangedEventArgs
                        {
                            Currency = walletAddressesGroup.Key,
                            Addresses = changedAddresses.ToArray()
                        });
                    }
                }
            }
            return upserted;
        }

        public Task<WalletAddress> GetAddressAsync(
            string currency,
            string address,
            string tokenContract = null,
            BigInteger? tokenId = null,
            CancellationToken cancellationToken = default)
        {
            return _liteDbLocalStorage.GetAddressAsync(
                currency,
                address,
                tokenContract,
                tokenId,
                cancellationToken);
        }

        public Task<WalletAddress> GetLastActiveWalletAddressAsync(
            string currency,
            string keyPath,
            int keyType,
            CancellationToken cancellationToken = default)
        {
            return _liteDbLocalStorage.GetLastActiveWalletAddressAsync(
                currency,
                keyPath,
                keyType,
                cancellationToken);
        }

        public Task<IEnumerable<WalletAddress>> GetUnspentAddressesAsync(
            string currency,
            string tokenContract = null,
            BigInteger? tokenId = null,
            bool includeUnconfirmed = true,
            CancellationToken cancellationToken = default)
        {
            return _liteDbLocalStorage.GetUnspentAddressesAsync(
                currency,
                tokenContract,
                tokenId,
                includeUnconfirmed,
                cancellationToken);
        }

        public Task<IEnumerable<WalletAddress>> GetAddressesAsync(
            string currency,
            string tokenContract = null,
            string address = null,
            CancellationToken cancellationToken = default)
        {
            return _liteDbLocalStorage.GetAddressesAsync(
                currency,
                tokenContract,
                address,
                cancellationToken);
        }

        #endregion Addresses

        #region Tokens

        public Task<int> UpsertTokenContractsAsync(
            IEnumerable<TokenContract> tokenContracts,
            CancellationToken cancellationToken = default)
        {
            return _liteDbLocalStorage.UpsertTokenContractsAsync(
                tokenContracts,
                cancellationToken);
        }

        public Task<IEnumerable<TokenContract>> GetTokenContractsAsync(
            CancellationToken cancellationToken = default)
        {
            return _liteDbLocalStorage.GetTokenContractsAsync(cancellationToken);
        }

        #endregion Tokens

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
            foreach (var txsGroup in txs.GroupBy(t => t.Currency))
            {
                var upsertResult = await _liteDbLocalStorage
                    .UpsertTransactionsAsync(txsGroup, notifyIfNewOrChanged, cancellationToken)
                    .ConfigureAwait(false);

                TransactionsChanged?.Invoke(this, new TransactionsChangedEventArgs(txsGroup));
            }

            return true;
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

        public Task<TransactionInfo<T, M>> GetTransactionWithMetadataByIdAsync<T, M>(
            string currency,
            string txId,
            CancellationToken cancellationToken = default)
            where T : ITransaction
            where M : ITransactionMetadata
        {
            return _liteDbLocalStorage.GetTransactionWithMetadataByIdAsync<T, M>(
                currency,
                txId,
                cancellationToken);
        }

        public Task<TransactionInfo<ITransaction, ITransactionMetadata>> GetTransactionWithMetadataByIdAsync(
            string currency,
            string txId,
            Type transactionType,
            Type metadataType,
            CancellationToken cancellationToken = default)
        {
            return _liteDbLocalStorage.GetTransactionWithMetadataByIdAsync(
                currency,
                txId,
                transactionType,
                metadataType,
                cancellationToken);
        }

        public Task<IEnumerable<T>> GetTransactionsAsync<T>(
            string currency,
            string tokenContract = null,
            int offset = 0,
            int limit = int.MaxValue,
            SortDirection sort = SortDirection.Desc,
            CancellationToken cancellationToken = default)
            where T : ITransaction
        {
            return _liteDbLocalStorage.GetTransactionsAsync<T>(
                currency,
                tokenContract,
                offset,
                limit,
                sort,
                cancellationToken);
        }

        public Task<IEnumerable<ITransaction>> GetTransactionsAsync(
            string currency,
            Type transactionType,
            string tokenContract = null,
            int offset = 0,
            int limit = int.MaxValue,
            SortDirection sort = SortDirection.Desc,
            CancellationToken cancellationToken = default)
        {
            return _liteDbLocalStorage.GetTransactionsAsync(
                currency,
                transactionType,
                tokenContract,
                offset,
                limit,
                sort,
                cancellationToken);
        }

        public Task<IEnumerable<TransactionInfo<T, M>>> GetTransactionsWithMetadataAsync<T, M>(
            string currency,
            string tokenContract = null,
            int offset = 0,
            int limit = int.MaxValue,
            SortDirection sort = SortDirection.Desc,
            CancellationToken cancellationToken = default)
            where T : ITransaction
            where M : ITransactionMetadata
        {
            return _liteDbLocalStorage.GetTransactionsWithMetadataAsync<T, M>(
                currency,
                tokenContract,
                offset,
                limit,
                sort,
                cancellationToken);
        }

        public Task<IEnumerable<TransactionInfo<ITransaction, ITransactionMetadata>>> GetTransactionsWithMetadataAsync(
            string currency,
            Type transactionType,
            Type metadataType,
            string tokenContract = null,
            int offset = 0,
            int limit = int.MaxValue,
            SortDirection sort = SortDirection.Desc,
            CancellationToken cancellationToken = default)
        {
            return _liteDbLocalStorage.GetTransactionsWithMetadataAsync(
                currency,
                transactionType,
                metadataType,
                tokenContract,
                offset,
                limit,
                sort,
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
            string currency,
            CancellationToken cancellationToken = default)
        {
            return _liteDbLocalStorage.RemoveTransactionByIdAsync(
                id,
                currency,
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

        public Task<int> RemoveTransactionsMetadataAsync(
            string currency,
            CancellationToken cancellationToken = default)
        {
            return _liteDbLocalStorage.RemoveTransactionsMetadataAsync(
                currency,
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
            return _liteDbLocalStorage
                .UpsertOrderAsync(order, cancellationToken);
        }

        public Task<bool> RemoveAllOrdersAsync(
            CancellationToken cancellationToken = default)
        {
            return _liteDbLocalStorage.RemoveAllOrdersAsync(
                cancellationToken);
        }

        public Order GetOrderById(
            string clientOrderId,
            CancellationToken cancellationToken = default)
        {
            return _liteDbLocalStorage.GetOrderById(
                clientOrderId,
                cancellationToken);
        }

        public Order GetOrderById(
            long id,
            CancellationToken cancellationToken = default)
        {
            return _liteDbLocalStorage
                .GetOrderById(id, cancellationToken);
        }

        public Task<bool> RemoveOrderByIdAsync(
            long id,
            CancellationToken cancellationToken = default)
        {
            return _liteDbLocalStorage
                .RemoveOrderByIdAsync(id, cancellationToken);
        }

        #endregion Orders

        #region Swaps

        public Task<bool> AddSwapAsync(
            Swap swap,
            CancellationToken cancellationToken = default)
        {
            //_swapById.Value.TryAdd(swap.Id, swap);

            return _liteDbLocalStorage
                .AddSwapAsync(swap, cancellationToken);
        }

        public Task<bool> UpdateSwapAsync(
            Swap swap,
            CancellationToken cancellationToken = default)
        {
            //_swapById.Value[swap.Id] = swap;

            return _liteDbLocalStorage
                .UpdateSwapAsync(swap, cancellationToken);
        }

        public async Task<Swap> GetSwapByIdAsync(
            long id,
            CancellationToken cancellationToken = default)
        {
            //if (_swapById.Value.TryGetValue(id, out var swap))
            //    return swap;

            return await _liteDbLocalStorage
                .GetSwapByIdAsync(id, cancellationToken)
                .ConfigureAwait(false);
        }

        public Task<IEnumerable<Swap>> GetSwapsAsync(
            int offset = 0,
            int limit = int.MaxValue,
            CancellationToken cancellationToken = default)
        {
            return _liteDbLocalStorage.GetSwapsAsync(
                offset,
                limit,
                cancellationToken);
        }

        public Task<Swap> GetSwapByPaymentTxIdAsync(
            string txId,
            CancellationToken cancellationToken = default)
        {
            return _liteDbLocalStorage.GetSwapByPaymentTxIdAsync(
                txId,
                cancellationToken);
        }

        #endregion Swaps
    }
}