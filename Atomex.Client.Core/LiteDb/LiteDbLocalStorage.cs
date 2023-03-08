using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Security;
using System.Threading;
using System.Threading.Tasks;

using LiteDB;
using Serilog;

using Atomex.Blockchain.Abstract;
using Atomex.Blockchain.Bitcoin;
using Atomex.Blockchain.Ethereum;
using Atomex.Blockchain.Tezos;
using Atomex.Blockchain.Tezos.Tzkt;
using Atomex.Common;
using Atomex.Core;
using Atomex.LiteDb.Migrations;
using Atomex.Wallet;
using Atomex.Wallet.Abstract;
using Network = Atomex.Core.Network;

namespace Atomex.LiteDb
{
    public class LiteDbLocalStorage : ILocalStorage
    {
        public const string OrdersCollectionName = "orders";
        public const string SwapsCollectionName = "swaps";
        public const string XtzTokensContractsCollectionName = "xtz_tokens_contracts";

        private const string CurrencyKey = nameof(WalletAddress.Currency);
        private const string AddressKey = nameof(WalletAddress.Address);
        private const string PaymentTxIdKey = nameof(Swap.PaymentTxId);
        private const string OrderIdKey = "OrderId";
        private const string UserMetadata = "UserMetadata";

        public event EventHandler<BalanceChangedEventArgs> BalanceChanged;
        public event EventHandler<TransactionsChangedEventArgs> TransactionsChanged;

        private readonly string _pathToDb;
        private string _sessionPassword;
        private readonly BsonMapper _bsonMapper;
        private LiteDatabase _db;

        public LiteDbLocalStorage(
            string pathToDb,
            SecureString password,
            Network network)
        {
            _pathToDb = pathToDb ??
                throw new ArgumentNullException(nameof(pathToDb));

            if (password == null)
                throw new ArgumentNullException(nameof(password));

            _sessionPassword = SessionPasswordHelper.GetSessionPassword(password);
            _bsonMapper = LiteDbMigration_11_to_12.CreateBsonMapper(network);

            var connectionString = $"FileName={_pathToDb};Password={_sessionPassword};Connection=direct;Upgrade=true";

            _db = new LiteDatabase(connectionString, _bsonMapper);
        }

        public static void CreateDataBase(string pathToDb, string sessionPassword, int version)
        {
            var connectionString = $"FileName={pathToDb};Password={sessionPassword};Connection=direct";

            using var db = new LiteDatabase(connectionString);

            db.UserVersion = version;
        }

        public void ChangePassword(SecureString newPassword)
        {
            var newSessionPassword = SessionPasswordHelper.GetSessionPassword(newPassword);

            _db.Rebuild(new LiteDB.Engine.RebuildOptions { Password = newSessionPassword });

            _sessionPassword = newSessionPassword;
        }

        public void Close()
        {
            _db?.Dispose();
            _db = null;
        }

        public bool IsXtzToken(string currency) =>
            currency == TezosHelper.Fa12 ||
            currency == TezosHelper.Fa2;

        public bool IsEthToken(string currency) =>
            currency == EthereumHelper.Erc20 ||
            currency == EthereumHelper.Erc721;

        public bool IsToken(string currency) =>
            IsEthToken(currency) || IsXtzToken(currency);

        private string GetAddressesCollectionName(string currency) =>
            $"{currency.ToLowerInvariant()}_addrs";

        private string GetTransactionsCollectionName(string currency) =>
            $"{currency.ToLowerInvariant()}_txs";

        private string GetTransactionsMetadataCollectionName(string currency) =>
            $"{currency.ToLowerInvariant()}_meta";

        #region Addresses

        public Task<bool> UpsertAddressAsync(
            WalletAddress walletAddress,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var document = _bsonMapper.ToDocument(walletAddress);

                var addressesCollectionName = GetAddressesCollectionName(walletAddress.Currency); 

                var addresses = _db.GetCollection(addressesCollectionName);
                //addresses.EnsureIndex(AddressKey);
                
                var upsertResult = addresses.Upsert(document);

                return Task.FromResult(upsertResult);
            }
            catch (Exception e)
            {
                Log.Error(e, "Error updating address");
            }

            return Task.FromResult(false);
        }

        public Task<int> UpsertAddressesAsync(
            IEnumerable<WalletAddress> walletAddresses,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var upserted = 0;

                foreach (var addressesGroup in walletAddresses.GroupBy(w => w.Currency))
                {
                    var documents = addressesGroup.Select(_bsonMapper.ToDocument);

                    var addressesCollectionName = GetAddressesCollectionName(addressesGroup.Key);

                    var addresses = _db.GetCollection(addressesCollectionName);
                    //addresses.EnsureIndex(AddressKey);

                    upserted += addresses.Upsert(documents);
                }

                return Task.FromResult(upserted);
            }
            catch (Exception e)
            {
                Log.Error(e, "Error updating addresses");
            }

            return Task.FromResult(0);
        }

        public Task<WalletAddress> GetAddressAsync(
            string currency,
            string address,
            string tokenContract = null,
            BigInteger? tokenId = null,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var id = WalletAddress.GetUniqueId(address, tokenContract, tokenId);

                var addressesCollectionName = GetAddressesCollectionName(currency);

                var document = _db
                    .GetCollection(addressesCollectionName)
                    .FindById(id);

                var walletAddress = document != null
                    ? _bsonMapper.ToObject<WalletAddress>(document)
                    : null;

                return Task.FromResult(walletAddress);
            }
            catch (Exception e)
            {
                Log.Error(e, "Error getting wallet address");
            }

            return Task.FromResult<WalletAddress>(null);
        }

        public Task<WalletAddress> GetLastActiveWalletAddressAsync(
            string currency,
            string keyPathPattern,
            int keyType,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var addressesCollectionName = GetAddressesCollectionName(currency);

                var document = _db
                    .GetCollection(addressesCollectionName)
                    .Query()
                    .Where("KeyType = @0 AND HasActivity = true", keyType)
                    .ToList()
                    .Where(d => d["KeyPath"].AsString.IsMatch(keyPathPattern))
                    .MaxByOrDefault(d => d["KeyIndex"].AsInt32);

                var walletAddress = document != null
                    ? _bsonMapper.ToObject<WalletAddress>(document)
                    : null;

                return Task.FromResult(walletAddress);
            }
            catch (Exception e)
            {
                Log.Error(e, "Error getting last active wallet address");
            }

            return Task.FromResult<WalletAddress>(null);
        }

        public Task<IEnumerable<WalletAddress>> GetUnspentAddressesAsync(
            string currency,
            string tokenContract = null,
            BigInteger? tokenId = null,
            bool includeUnconfirmed = true,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var addressesCollectionName = GetAddressesCollectionName(currency);

                var query = _db
                    .GetCollection(addressesCollectionName)
                    .Query();

                if (tokenContract != null)
                    query.Where("TokenBalance.Contract = @0", tokenContract);

                if (tokenId != null)
                    query.Where("TokenBalance.TokenId = @0", tokenId.Value.ToString());

                const string ZeroBalance = "0";

                if (includeUnconfirmed) {
                    query.Where("(Balance != @0 OR UnconfirmedIncome != @0 OR UnconfirmedOutcome != @0)", ZeroBalance);
                } else {
                    query.Where("Balance != @0", ZeroBalance);
                }

                var unspentAddresses = query
                    .ToList()
                    .Select(_bsonMapper.ToObject<WalletAddress>)
                    .ToList();

                return Task.FromResult<IEnumerable<WalletAddress>>(unspentAddresses);
            }
            catch (Exception e)
            {
                Log.Error(e, "Error getting unspent wallet addresses");
            }

            return Task.FromResult(Enumerable.Empty<WalletAddress>());
        }

        public Task<IEnumerable<WalletAddress>> GetAddressesAsync(
            string currency,
            string tokenContract = null,
            string address = null,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var addressesCollectionName = GetAddressesCollectionName(currency);

                var query = _db
                    .GetCollection(addressesCollectionName)
                    .Query();

                if (tokenContract != null)
                    query.Where("TokenBalance.Contract = @0", tokenContract);

                if (address != null)
                    query.Where("Address = @0", address);

                var unspentAddresses = query
                    .ToList()
                    .Select(_bsonMapper.ToObject<WalletAddress>)
                    .ToList();

                return Task.FromResult<IEnumerable<WalletAddress>>(unspentAddresses);
            }
            catch (Exception e)
            {
                Log.Error(e, "Error getting wallet addresses");
            }

            return Task.FromResult(Enumerable.Empty<WalletAddress>());
        }

        #endregion Addresses

        #region TezosTokens

        public Task<int> UpsertTokenContractsAsync(
            IEnumerable<TokenContract> tokenContracts,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var documents = tokenContracts
                    .Select(t => _bsonMapper.ToDocument(t));

                var upserted = _db
                    .GetCollection(XtzTokensContractsCollectionName)
                    .Upsert(documents);

                return Task.FromResult(upserted);
            }
            catch (Exception e)
            {
                Log.Error(e, "Error adding contracts");
            }

            return Task.FromResult(0);
        }

        public Task<IEnumerable<TokenContract>> GetTokenContractsAsync(
            CancellationToken cancellationToken = default)
        {
            try
            {
                var contracts = _db
                    .GetCollection(XtzTokensContractsCollectionName)
                    .FindAll()
                    .Select(d => new TokenContract
                    {
                        Address = d["Address"].AsString,
                        Name = d["Name"].AsString,
                        Type = d.ContainsKey("Type")
                            ? d["Type"].AsString
                            : GetContractType(d),
                    })
                    .ToList();

                return Task.FromResult<IEnumerable<TokenContract>>(contracts);
            }
            catch (Exception e)
            {
                Log.Error(e, "Error getting tezos tokens contracts");
            }

            return Task.FromResult(Enumerable.Empty<TokenContract>());
        }

        private string GetContractType(BsonDocument d)
        {
            var contractTags = d.ContainsKey("ContractTags")
                ? d["ContractTags"].AsArray
                    .Select(v => v.AsString)
                    .ToList()
                : null;

            if (contractTags != null)
            {
                if (contractTags.Contains("fa2"))
                    return TezosHelper.Fa2;

                if (contractTags.Contains("fa1-2"))
                    return TezosHelper.Fa12;
            }

            var interfaces = d.ContainsKey("Interfaces")
                ? d["Interfaces"].AsArray
                    .Select(v => v.AsString)
                    .ToList()
                : null;

            if (interfaces == null)
                return TezosHelper.Fa2;

            if (interfaces.FirstOrDefault(i => i == "TZIP-12" || i == "TZIP-012" || i.StartsWith("TZIP-012")) != null)
                return TezosHelper.Fa2;

            if (interfaces.FirstOrDefault(i => i == "TZIP-7" || i == "TZIP-007" || i.StartsWith("TZIP-007")) != null)
                return TezosHelper.Fa12;

            return TezosHelper.Fa2;
        }

        #endregion TezosTokens

        #region Transactions

        public Task<bool> UpsertTransactionAsync(
            ITransaction tx,
            bool notifyIfNewOrChanged = false,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var transactionsCollectionName = GetTransactionsCollectionName(tx.Currency);
                var transactionsMetadataCollectionName = GetTransactionsMetadataCollectionName(tx.Currency);

                var transactions = _db
                    .GetCollection(transactionsCollectionName);

                var document = _bsonMapper.ToDocument(tx);

                document[UserMetadata] = new BsonDocument
                {
                    ["$id"] = document["_id"],
                    ["$ref"] = transactionsMetadataCollectionName
                };

                var result = transactions.Upsert(document);

                // todo: notify if add or changed

                return Task.FromResult(result);
            }
            catch (Exception e)
            {
                Log.Error(e, "Error adding transaction");
            }

            return Task.FromResult(false);
        }

        public Task<bool> UpsertTransactionsAsync(
            IEnumerable<ITransaction> txs,
            bool notifyIfNewOrChanged = false,
            CancellationToken cancellationToken = default)
        {
            try
            {
                foreach (var txsGroup in txs.GroupBy(t => t.Currency))
                {
                    var transactionsCollectionName = GetTransactionsCollectionName(txsGroup.Key);
                    var transactionsMetadataCollectionName = GetTransactionsMetadataCollectionName(txsGroup.Key);

                    var transactions = _db
                        .GetCollection(transactionsCollectionName);

                    var documents = txsGroup
                        .Select(tx =>
                        {
                            var d = _bsonMapper.ToDocument(tx);

                            d[UserMetadata] = new BsonDocument
                            {
                                ["$id"] = d["_id"],
                                ["$ref"] = transactionsMetadataCollectionName
                            };

                            return d;
                        })
                        .ToList();

                    var result = transactions.Upsert(documents);
                }

                // todo: notify if add or changed

                return Task.FromResult(true);
            }
            catch (Exception e)
            {
                Log.Error(e, "Error adding transactions");
            }

            return Task.FromResult(false);
        }

        public async Task<T> GetTransactionByIdAsync<T>(
            string currency,
            string txId,
            CancellationToken cancellationToken = default) where T : ITransaction
        {
            return (T)await GetTransactionByIdAsync(currency, txId, typeof(T), cancellationToken)
                .ConfigureAwait(false);
        }

        public Task<ITransaction> GetTransactionByIdAsync(
            string currency,
            string txId,
            Type transactionType,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var transactionsCollectionName = GetTransactionsCollectionName(currency);

                var document = _db
                    .GetCollection(transactionsCollectionName)
                    .FindById(txId);

                if (document != null)
                {
                    var tx = (ITransaction)_bsonMapper.ToObject(transactionType, document);

                    return Task.FromResult(tx);
                }
            }
            catch (Exception e)
            {
                Log.Error(e, "Error getting transaction by id");
            }

            return Task.FromResult<ITransaction>(default);
        }

        public async Task<TransactionInfo<T, M>> GetTransactionWithMetadataByIdAsync<T, M>(
            string currency,
            string txId,
            CancellationToken cancellationToken = default)
            where T : ITransaction
            where M : ITransactionMetadata
        {
            var txInfo = await GetTransactionWithMetadataByIdAsync(currency, txId, typeof(T), typeof(M),cancellationToken)
                .ConfigureAwait(false);

            return new TransactionInfo<T, M> { Tx = (T)txInfo.Tx, Metadata = (M)txInfo.Metadata };
        }

        public Task<TransactionInfo<ITransaction, ITransactionMetadata>> GetTransactionWithMetadataByIdAsync(
            string currency,
            string txId,
            Type transactionType,
            Type metadataType,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var transactionsCollectionName = GetTransactionsCollectionName(currency);

                var document = _db
                    .GetCollection(transactionsCollectionName)
                    .Include(UserMetadata)
                    .FindById(txId);

                if (document != null)
                {
                    var tx = (ITransaction)_bsonMapper.ToObject(transactionType, document);
                    var metadata = !document[UserMetadata].AsDocument.ContainsKey("$missing")
                        ? (ITransactionMetadata)_bsonMapper.ToObject(metadataType, document[UserMetadata].AsDocument)
                        : null;

                    return Task.FromResult(new TransactionInfo<ITransaction, ITransactionMetadata> { Tx = tx, Metadata = metadata });
                }
            }
            catch (Exception e)
            {
                Log.Error(e, "Error getting transaction by id");
            }

            return Task.FromResult<TransactionInfo<ITransaction, ITransactionMetadata>>(default);
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
            try
            {
                var transactionsCollectionName = GetTransactionsCollectionName(currency);

                var query = _db
                    .GetCollection(transactionsCollectionName)
                    .Query();

                if (tokenContract != null)
                    query.Where("Contract = @0", tokenContract);

                var transactions = query
                    .OrderBy("CreationTime", sort == SortDirection.Desc ? -1 : 1)
                    .Offset(offset)
                    .Limit(limit)
                    .ToList()
                    .Select(d => (ITransaction)_bsonMapper.ToObject(transactionType, d))
                    .ToList();

                return Task.FromResult<IEnumerable<ITransaction>>(transactions);
            }
            catch (Exception e)
            {
                Log.Error(e, $"Error getting transactions for {currency}");
            }

            return Task.FromResult(Enumerable.Empty<ITransaction>());
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
            try
            {
                var transactionsCollectionName = GetTransactionsCollectionName(currency);

                var query = _db
                    .GetCollection(transactionsCollectionName)
                    .Query();

                if (tokenContract != null)
                    query.Where("Contract = @0", tokenContract);

                var transactions = query
                    .OrderBy("CreationTime", sort == SortDirection.Desc ? -1 : 1)
                    .Offset(offset)
                    .Limit(limit)
                    .ToList()
                    .Select(_bsonMapper.ToObject<T>)
                    .ToList();

                return Task.FromResult<IEnumerable<T>>(transactions);
            }
            catch (Exception e)
            {
                Log.Error(e, $"Error getting transactions for {currency}");
            }

            return Task.FromResult(Enumerable.Empty<T>());
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
            try
            {
                var transactionsCollectionName = GetTransactionsCollectionName(currency);

                var query = _db
                    .GetCollection(transactionsCollectionName)
                    .Query()
                    .Include(UserMetadata);

                if (tokenContract != null)
                    query.Where("Contract = @0", tokenContract);

                var transactions = query
                    .OrderBy("CreationTime", sort == SortDirection.Desc ? -1 : 1)
                    .Offset(offset)
                    .Limit(limit)
                    .ToList()
                    .Select(d =>
                    {
                        var tx = (ITransaction)_bsonMapper.ToObject(transactionType, d);
                        var metadata = !d[UserMetadata].AsDocument.ContainsKey("$missing")
                            ? (ITransactionMetadata)_bsonMapper.ToObject(metadataType, d[UserMetadata].AsDocument)
                            : null;

                        return new TransactionInfo<ITransaction, ITransactionMetadata> { Tx = tx, Metadata = metadata };
                    })
                    .ToList();

                return Task.FromResult<IEnumerable<TransactionInfo<ITransaction, ITransactionMetadata>>>(transactions);
            }
            catch (Exception e)
            {
                Log.Error(e, $"Error getting transactions with metadata for {currency}");
            }

            return Task.FromResult(Enumerable.Empty<TransactionInfo<ITransaction, ITransactionMetadata>>());
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
            try
            {
                var transactionsCollectionName = GetTransactionsCollectionName(currency);

                var query = _db
                    .GetCollection(transactionsCollectionName)
                    .Query()
                    .Include(UserMetadata);

                if (tokenContract != null)
                    query.Where("Contract = @0", tokenContract);

                var transactions = query
                    .OrderBy("CreationTime", sort == SortDirection.Desc ? -1 : 1)
                    .Offset(offset)
                    .Limit(limit)
                    .ToList()
                    .Select(d =>
                    {
                        var tx = _bsonMapper.ToObject<T>(d);
                        var metadata = !d[UserMetadata].AsDocument.ContainsKey("$missing")
                            ? _bsonMapper.ToObject<M>(d[UserMetadata].AsDocument)
                            : default;

                        return new TransactionInfo<T, M> { Tx = tx, Metadata = metadata };
                    })
                    .ToList();

                return Task.FromResult<IEnumerable<TransactionInfo<T, M>>>(transactions);
            }
            catch (Exception e)
            {
                Log.Error(e, $"Error getting transactions with metadata for {currency}");
            }

            return Task.FromResult(Enumerable.Empty<TransactionInfo<T, M>>());
        }

        public Task<IEnumerable<T>> GetUnconfirmedTransactionsAsync<T>(
            string currency,
            CancellationToken cancellationToken = default) where T : ITransaction
        {
            try
            {
                var transactionsCollectionName = GetTransactionsCollectionName(currency);

                var documents = _db
                    .GetCollection(transactionsCollectionName)
                    .Query()
                    .Where("Confirmations = 0")
                    .ToList();

                var transactions = documents
                    .Select(_bsonMapper.ToObject<T>)
                    .ToList();

                return Task.FromResult<IEnumerable<T>>(transactions);
            }
            catch (Exception e)
            {
                Log.Error(e, $"Error getting unconfirmed transactions for {currency}");
            }

            return Task.FromResult(Enumerable.Empty<T>());
        }

        public Task<bool> RemoveTransactionByIdAsync(
            string id,
            string currency,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var transactionsCollectionName = GetTransactionsCollectionName(currency);

                var transactions = _db
                    .GetCollection(transactionsCollectionName);

                return Task.FromResult(transactions.Delete(id));
            }
            catch (Exception e)
            {
                Log.Error(e, "Error removing transaction");
            }

            return Task.FromResult(false);
        }

        public Task<bool> UpsertTransactionsMetadataAsync(
            IEnumerable<ITransactionMetadata> metadata,
            bool notifyIfNewOrChanged = false,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var transactionsMetadataCollectionName = GetTransactionsMetadataCollectionName(metadata.First().Currency);

                var transactionTypes = _db
                    .GetCollection(transactionsMetadataCollectionName);

                var result = transactionTypes.Upsert(metadata.Select(t => _bsonMapper.ToDocument(t)));

                // todo: notify if add or changed

                return Task.FromResult(true);
            }
            catch (Exception e)
            {
                Log.Error(e, "Error adding transaction types");
            }

            return Task.FromResult(false);
        }

        public async Task<T> GetTransactionMetadataByIdAsync<T>(
            string currency,
            string txId,
            CancellationToken cancellationToken = default)
            where T : ITransactionMetadata
        {
            return (T)await GetTransactionMetadataByIdAsync(currency, txId, typeof(T), cancellationToken)
                .ConfigureAwait(false);
        }

        public Task<ITransactionMetadata> GetTransactionMetadataByIdAsync(
            string currency,
            string txId,
            Type type,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var transactionsMetadataCollectionName = GetTransactionsMetadataCollectionName(currency);

                var document = _db
                    .GetCollection(transactionsMetadataCollectionName)
                    .FindById(txId);

                if (document != null)
                {
                    var txType = (ITransactionMetadata)_bsonMapper.ToObject(type, document);

                    return Task.FromResult(txType);
                }
            }
            catch (Exception e)
            {
                Log.Error(e, "Error getting transaction metadata by id");
            }

            return Task.FromResult<ITransactionMetadata>(default);
        }

        public Task<int> RemoveTransactionsMetadataAsync(
            string currency,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var transactionsMetadataCollectionName = GetTransactionsMetadataCollectionName(currency);

                var deleted = _db
                    .GetCollection(transactionsMetadataCollectionName)
                    .DeleteAll();

                return Task.FromResult(deleted);
            }
            catch (Exception e)
            {
                Log.Error(e, "Error removing transactions metadata for currency {@currency}", currency);
            }

            return Task.FromResult(0);
        }

        #endregion Transactions

        #region Outputs

        public Task<bool> UpsertOutputsAsync(
            IEnumerable<BitcoinTxOutput> outputs,
            string currency,
            NBitcoin.Network network,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var outputsCollectionName = $"{currency}_outs";

                var documents = outputs
                    .Select(o =>
                    {
                        var document = _bsonMapper.ToDocument(o);
                        document[AddressKey] = o.DestinationAddress(network);
                        return document;
                    });

                var outputsCollection = _db
                    .GetCollection(outputsCollectionName);

                outputsCollection.EnsureIndex(CurrencyKey);
                outputsCollection.Upsert(documents);

                return Task.FromResult(true);
            }
            catch (Exception e)
            {
                Log.Error(e, "Error while upserting outputs");
            }

            return Task.FromResult(false);
        }

        public Task<IEnumerable<BitcoinTxOutput>> GetAvailableOutputsAsync(
            string currency,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var outputsCollectionName = $"{currency}_outs";

                var outputs = _db
                    .GetCollection(outputsCollectionName)
                    .Query()
                    .Where("SpentTxPoints = null")
                    .ToList()
                    .Select(_bsonMapper.ToObject<BitcoinTxOutput>)
                    .ToList();

                return Task.FromResult<IEnumerable<BitcoinTxOutput>>(outputs);
            }
            catch (Exception e)
            {
                Log.Error(e, "Error getting outputs");
            }

            return Task.FromResult(Enumerable.Empty<BitcoinTxOutput>());
        }

        public Task<IEnumerable<BitcoinTxOutput>> GetAvailableOutputsAsync(
            string currency,
            string address,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var outputsCollectionName = $"{currency}_outs";

                var outputs = _db
                    .GetCollection(outputsCollectionName)
                    .Query()
                    .Where("Address = @0 AND SpentTxPoints = null", address)
                    .ToList()
                    .Select(_bsonMapper.ToObject<BitcoinTxOutput>)
                    .ToList();

                return Task.FromResult<IEnumerable<BitcoinTxOutput>>(outputs);
            }
            catch (Exception e)
            {
                Log.Error(e, "Error getting outputs");
            }

            return Task.FromResult(Enumerable.Empty<BitcoinTxOutput>());
        }

        public Task<IEnumerable<BitcoinTxOutput>> GetOutputsAsync(
            string currency,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var outputsCollectionName = $"{currency}_outs";

                var outputs = _db
                    .GetCollection(outputsCollectionName)
                    .FindAll()
                    .Select(_bsonMapper.ToObject<BitcoinTxOutput>)
                    .ToList();

                return Task.FromResult<IEnumerable<BitcoinTxOutput>>(outputs);
            }
            catch (Exception e)
            {
                Log.Error(e, "Error getting outputs");
            }

            return Task.FromResult(Enumerable.Empty<BitcoinTxOutput>());
        }

        public Task<IEnumerable<BitcoinTxOutput>> GetOutputsAsync(
            string currency,
            string address,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var outputsCollectionName = $"{currency}_outs";

                var outputs = _db
                    .GetCollection(outputsCollectionName)
                    .Query()
                    .Where("Address = @0", address)
                    .ToList()
                    .Select(_bsonMapper.ToObject<BitcoinTxOutput>)
                    .ToList();

                return Task.FromResult<IEnumerable<BitcoinTxOutput>>(outputs);
            }
            catch (Exception e)
            {
                Log.Error(e, "Error getting outputs");
            }

            return Task.FromResult(Enumerable.Empty<BitcoinTxOutput>());
        }

        public Task<BitcoinTxOutput> GetOutputAsync(
            string currency,
            string txId,
            uint index,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var outputsCollectionName = $"{currency}_outs";

                var outputDocument = _db
                    .GetCollection(outputsCollectionName)
                    .FindById($"{txId}:{index}");

                var output = outputDocument != null
                    ? _bsonMapper.ToObject<BitcoinTxOutput>(outputDocument)
                    : null;

                return Task.FromResult(output);
            }
            catch (Exception e)
            {
                Log.Error(e, "Error getting output");
            }

            return Task.FromResult<BitcoinTxOutput>(null);
        }

        #endregion Outputs

        #region Orders

        public Task<bool> UpsertOrderAsync(
            Order order,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var localOrder = GetOrderByIdUnsync(order.ClientOrderId);

                if (!order.VerifyOrder(localOrder))
                    return Task.FromResult(false);

                if (localOrder != null)
                    order.ForwardLocalParameters(localOrder);

                var document = _bsonMapper.ToDocument(order);

                var result = _db
                    .GetCollection(OrdersCollectionName)
                    .Upsert(document);

                return Task.FromResult(result);
            }
            catch (Exception e)
            {
                Log.Error(e, "Error adding order");
            }

            return Task.FromResult(false);
        }

        public Task<bool> RemoveAllOrdersAsync(
            CancellationToken cancellationToken = default)
        {
            try
            {
                var collectionExists = _db.DropCollection(OrdersCollectionName);

                if (collectionExists)
                    Log.Debug("The {Collection} collection is dropped", OrdersCollectionName);
                else
                    Log.Debug("The {Collection} collection does not exist. Nothing to drop", OrdersCollectionName);

                return Task.FromResult(true);
            }
            catch (Exception e)
            {
                Log.Error(e, "Dropping the {Collection} collection failed", OrdersCollectionName);
            }

            return Task.FromResult(false);
        }

        public Order GetOrderById(
            string clientOrderId,
            CancellationToken cancellationToken = default)
        {
            return GetOrderByIdUnsync(clientOrderId);
        }

        private Order GetOrderByIdUnsync(string clientOrderId)
        {
            try
            {
                var document = _db
                    .GetCollection(OrdersCollectionName)
                    .FindById(clientOrderId);

                return document != null
                    ? _bsonMapper.ToObject<Order>(document)
                    : null;
            }
            catch (Exception e)
            {
                Log.Error(e, "Error getting order by id");

                return null;
            }
        }

        public Order GetOrderById(
            long id,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var documents = _db
                    .GetCollection(OrdersCollectionName)
                    .Find(Query.EQ(OrderIdKey, id));

                return documents != null && documents.Any()
                    ? _bsonMapper.ToObject<Order>(documents.First())
                    : null;
            }
            catch (Exception e)
            {
                Log.Error(e, "Error getting order by id");

                return null;
            }
        }

        public Task<bool> RemoveOrderByIdAsync(
            long id,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var removed = _db
                    .GetCollection(OrdersCollectionName)
                    .DeleteMany(Query.EQ(OrderIdKey, id));

                return Task.FromResult(removed > 0);
            }
            catch (Exception e)
            {
                Log.Error(e, "Error while remove order by id");

                return Task.FromResult(false);
            }
        }

        #endregion Orders

        #region Swaps

        public Task<bool> AddSwapAsync(
            Swap swap,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var _ = _db
                    .GetCollection<Swap>(SwapsCollectionName)
                    .Insert(swap);

                return Task.FromResult(true);
            }
            catch (Exception e)
            {
                Log.Error(e, "Error adding swap");
            }

            return Task.FromResult(false);
        }

        public Task<bool> UpdateSwapAsync(
            Swap swap,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var result = _db
                    .GetCollection<Swap>(SwapsCollectionName)
                    .Update(swap);

                return Task.FromResult(result);
            }
            catch (Exception e)
            {
                Log.Error(e, "Swap update error");
            }

            return Task.FromResult(false);
        }

        public Task<Swap> GetSwapByIdAsync(
            long id,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var swap = _db
                    .GetCollection<Swap>(SwapsCollectionName)
                    .FindById(id);

                return Task.FromResult(swap);
            }
            catch (Exception e)
            {
                Log.Error(e, "Error getting swap by id");
            }

            return Task.FromResult<Swap>(null);
        }

        public Task<IEnumerable<Swap>> GetSwapsAsync(
            int offset = 0,
            int limit = int.MaxValue,
            SortDirection sort = SortDirection.Desc,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var swaps = _db
                    .GetCollection<Swap>(SwapsCollectionName)
                    .Query()
                    .OrderBy(s => s.TimeStamp, sort == SortDirection.Desc ? -1 : 1)
                    .Offset(offset)
                    .Limit(limit)
                    .ToList();

                return Task.FromResult<IEnumerable<Swap>>(swaps);
            }
            catch (Exception e)
            {
                Log.Error(e, "Swaps getting error");
            }

            return Task.FromResult(Enumerable.Empty<Swap>());
        }

        public Task<Swap> GetSwapByPaymentTxIdAsync(
            string txId,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var swap = _db
                    .GetCollection<Swap>(SwapsCollectionName)
                    .FindOne(Query.EQ(PaymentTxIdKey, txId));

                return Task.FromResult(swap);
            }
            catch (Exception e)
            {
                Log.Error(e, "Get swap by payment tx id error");
            }

            return Task.FromResult<Swap>(null);
        }

        #endregion Swaps
    }
}