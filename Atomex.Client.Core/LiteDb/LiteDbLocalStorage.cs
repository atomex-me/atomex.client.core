using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Security;
using System.Threading;
using System.Threading.Tasks;

using LiteDB;
using Serilog;

using Atomex.Blockchain;
using Atomex.Blockchain.Abstract;
using Atomex.Blockchain.Bitcoin;
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
        public const string OrdersCollectionName = "Orders";
        public const string SwapsCollectionName = "Swaps";
        public const string TransactionCollectionName = "Transactions";
        public const string TransactionMetadataCollectionName = "TransactionsMetadata";
        public const string OutputsCollectionName = "Outputs";
        public const string AddressesCollectionName = "Addresses";
        public const string TezosTokensAddresses = "TezosTokensAddresses";
        public const string TezosTokensTransfers = "TezosTokensTransfers";
        public const string TezosTokensContracts = "TezosTokensContracts";

        private const string CurrencyKey = nameof(WalletAddress.Currency);
        private const string AddressKey = nameof(WalletAddress.Address);
        private const string BalanceKey = nameof(WalletAddress.Balance);
        private const string UnconfirmedIncomeKey = nameof(WalletAddress.UnconfirmedIncome);
        private const string UnconfirmedOutcomeKey = nameof(WalletAddress.UnconfirmedOutcome);
        private const string HasActivityKey = nameof(WalletAddress.HasActivity);
        private const string TokenContractKey = nameof(TokenBalance) + "." + nameof(TokenBalance.Contract);
        private const string TokenIdKey = nameof(TokenBalance) + "." + nameof(TokenBalance.TokenId);
        private const string TransferContract = nameof(TezosTokenTransfer.Contract);
        private const string KeyTypeKey = nameof(WalletAddress.KeyType);
        private const string KeyPathKey = nameof(WalletAddress.KeyPath);
        private const string KeyIndexKey = nameof(WalletAddress.KeyIndex);
        private const string PaymentTxIdKey = nameof(Swap.PaymentTxId);
        private const string OrderIdKey = "OrderId";
        private const string OutputTxIdKey = nameof(BitcoinTxOutput.TxId);
        private const string OutputIndexKey = nameof(BitcoinTxOutput.Index);
        private const string TokenContractAddressKey = nameof(TokenContract.Address);
        private const string TokenContractNameKey = nameof(TokenContract.Name);
        private const string TokenContractTypeKey = nameof(TokenContract.Type);
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

        #region Addresses

        public Task<bool> UpsertAddressAsync(
            WalletAddress walletAddress,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var document = _bsonMapper.ToDocument(walletAddress);

                var addresses = _db.GetCollection(AddressesCollectionName);
                //addresses.EnsureIndex(IndexKey);
                addresses.EnsureIndex(CurrencyKey);
                addresses.EnsureIndex(AddressKey);
                
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
                var documents = walletAddresses.Select(_bsonMapper.ToDocument);

                var addresses = _db.GetCollection(AddressesCollectionName);
                //addresses.EnsureIndex(IndexKey);
                addresses.EnsureIndex(CurrencyKey);
                addresses.EnsureIndex(AddressKey);
                
                var upsertResult = addresses.Upsert(documents);

                return Task.FromResult(upsertResult);
            }
            catch (Exception e)
            {
                Log.Error(e, "Error updating addresses");
            }

            return Task.FromResult(0);
        }

        public Task<WalletAddress> GetWalletAddressAsync(
            string currency,
            string address,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var id = WalletAddress.GetUniqueId(address, currency);

                var document = _db
                    .GetCollection(AddressesCollectionName)
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
                var document = _db.GetCollection(AddressesCollectionName)
                    .Find(Query.And(
                        Query.EQ(CurrencyKey, currency),
                        Query.EQ(KeyTypeKey, keyType),
                        Query.EQ(HasActivityKey, true)))
                    .Where(d => d[KeyPathKey].AsString.IsMatch(keyPathPattern))
                    .MaxByOrDefault(d => d[KeyIndexKey].AsInt32);

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
            bool includeUnconfirmed = true,
            CancellationToken cancellationToken = default)
        {
            var query = includeUnconfirmed
                ? Query.And(
                    Query.EQ(CurrencyKey, currency),
                    Query.Or(
                        Query.Not(BalanceKey, 0m),
                        Query.Not(UnconfirmedIncomeKey, 0m),
                        Query.Not(UnconfirmedOutcomeKey, 0m))
                    )
                : Query.And(
                    Query.EQ(CurrencyKey, currency),
                    Query.GT(BalanceKey, 0m));

            try
            {
                var unspentAddresses = _db
                    .GetCollection(AddressesCollectionName)
                    .Find(query)
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
            CancellationToken cancellationToken = default)
        {
            try
            {
                var unspentAddresses = _db
                    .GetCollection(AddressesCollectionName)
                    .Find(Query.EQ(CurrencyKey, currency))
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

        public Task<WalletAddress> GetTokenAddressAsync(
            string currency,
            string tokenContract,
            BigInteger tokenId,
            string address,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var id = WalletAddress.GetUniqueId(address, currency, tokenContract, tokenId);

                var document = _db
                    .GetCollection(TezosTokensAddresses)
                    .FindById(id);

                var walletAddress = document != null
                    ? _bsonMapper.ToObject<WalletAddress>(document)
                    : null;

                return Task.FromResult(walletAddress);
            }
            catch (Exception e)
            {
                Log.Error(e, "Error getting token wallet address");
            }

            return Task.FromResult<WalletAddress>(null);
        }

        public Task<IEnumerable<WalletAddress>> GetTokenAddressesAsync(
            CancellationToken cancellationToken = default)
        {
            try
            {
                var addresses = _db
                    .GetCollection(TezosTokensAddresses)
                    .FindAll()
                    .Select(_bsonMapper.ToObject<WalletAddress>)
                    .ToList();

                return Task.FromResult<IEnumerable<WalletAddress>>(addresses);
            }
            catch (Exception e)
            {
                Log.Error(e, "Error getting tezos tokens addresses");
            }

            return Task.FromResult(Enumerable.Empty<WalletAddress>());
        }

        public Task<IEnumerable<WalletAddress>> GetTokenAddressesAsync(
            string address,
            string tokenContract,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var addresses = _db
                    .GetCollection(TezosTokensAddresses)
                    .Find(Query.And(
                        Query.EQ(AddressKey, address),
                        Query.EQ(TokenContractKey, tokenContract)))
                    .Select(_bsonMapper.ToObject<WalletAddress>)
                    .ToList();

                return Task.FromResult<IEnumerable<WalletAddress>>(addresses);
            }
            catch (Exception e)
            {
                Log.Error(e, "Error getting tezos tokens addresses");
            }

            return Task.FromResult(Enumerable.Empty<WalletAddress>());
        }

        public Task<IEnumerable<WalletAddress>> GetTokenAddressesByContractAsync(
            string tokenContract,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var addresses = _db
                    .GetCollection(TezosTokensAddresses)
                    .Find(Query.EQ(TokenContractKey, tokenContract))
                    .Select(_bsonMapper.ToObject<WalletAddress>)
                    .ToList();

                return Task.FromResult<IEnumerable<WalletAddress>>(addresses);
            }
            catch (Exception e)
            {
                Log.Error(e, "Error getting tezos tokens addresses");
            }

            return Task.FromResult(Enumerable.Empty<WalletAddress>());
        }

        public Task<int> UpsertTokenAddressesAsync(
            IEnumerable<WalletAddress> walletAddresses,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var documents = walletAddresses.Select(_bsonMapper.ToDocument);

                var addresses = _db.GetCollection(TezosTokensAddresses);
                //addresses.EnsureIndex(IndexKey);
                //addresses.EnsureIndex(CurrencyKey);
                //addresses.EnsureIndex(AddressKey);

                var result = addresses.Upsert(documents);

                return Task.FromResult(result);
            }
            catch (Exception e)
            {
                Log.Error(e, "Error updating tezos token addresses");
            }

            return Task.FromResult(0);
        }

        public Task<IEnumerable<WalletAddress>> GetUnspentTokenAddressesAsync(
            string currency,
            string tokenContract,
            decimal tokenId,
            CancellationToken cancellationToken = default)
        {
            var queries = new List<BsonExpression>
            {
                Query.EQ(TokenContractKey, tokenContract),
                Query.Or(
                    Query.Not(BalanceKey, 0m),
                    Query.Not(UnconfirmedIncomeKey, 0m),
                    Query.Not(UnconfirmedOutcomeKey, 0m))
            };

            if (tokenId != 0)
                queries.Insert(1, Query.EQ(TokenIdKey, tokenId));

            var query = Query.And(queries.ToArray());

            try
            {
                var unspentAddresses = _db
                    .GetCollection(TezosTokensAddresses)
                    .Find(query)
                    .Select(_bsonMapper.ToObject<WalletAddress>)
                    .ToList();

                return Task.FromResult<IEnumerable<WalletAddress>>(unspentAddresses);
            }
            catch (Exception e)
            {
                Log.Error(e, "Error getting unspent tezos tokens wallet addresses");
            }

            return Task.FromResult(Enumerable.Empty<WalletAddress>());
        }

        public Task<int> UpsertTokenTransfersAsync(
            IEnumerable<TezosTokenTransfer> tokenTransfers,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var documents = tokenTransfers
                    .Select(t =>
                    {
                        var d = _bsonMapper.ToDocument(t);

                        d[UserMetadata] = new BsonDocument
                        {
                            ["$id"] = d["_id"],
                            ["$ref"] = TransactionMetadataCollectionName
                        };

                        return d;
                    });

                var upserted = _db
                    .GetCollection(TezosTokensTransfers)
                    .Upsert(documents);

                return Task.FromResult(upserted);
            }
            catch (Exception e)
            {
                Log.Error(e, "Error adding transfers");
            }

            return Task.FromResult(0);
        }

        public Task<IEnumerable<TezosTokenTransfer>> GetTokenTransfersAsync(
            string contractAddress,
            int offset = 0,
            int limit = int.MaxValue,
            SortDirection sort = SortDirection.Desc,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var transfers = _db
                    .GetCollection(TezosTokensTransfers)
                    .Query()
                    .Where($"Contract = @0", contractAddress)
                    .OrderBy("CreationTime", sort == SortDirection.Desc ? -1 : 1)
                    .Offset(offset)
                    .Limit(limit)
                    .ToList()
                    .Select(_bsonMapper.ToObject<TezosTokenTransfer>)
                    .ToList();

                return Task.FromResult<IEnumerable<TezosTokenTransfer>>(transfers);
            }
            catch (Exception e)
            {
                Log.Error(e, "Error getting tezos tokens transfers");
            }

            return Task.FromResult(Enumerable.Empty<TezosTokenTransfer>());
        }

        public Task<IEnumerable<(TezosTokenTransfer Transfer, TransactionMetadata Metadata)>> GetTokenTransfersWithMetadataAsync(
            string contractAddress,
            int offset = 0,
            int limit = int.MaxValue,
            SortDirection sort = SortDirection.Desc,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var transfers = _db
                    .GetCollection(TezosTokensTransfers)
                    .Query()
                    .Include(UserMetadata)
                    .Where($"Contract = @0", contractAddress)
                    .OrderBy("CreationTime", sort == SortDirection.Desc ? -1 : 1)
                    .Offset(offset)
                    .Limit(limit)
                    .ToList()
                    .Select(d =>
                    {
                        var tx = _bsonMapper.ToObject<TezosTokenTransfer>(d);
                        var metadata = !d[UserMetadata].AsDocument.ContainsKey("$missing")
                            ? _bsonMapper.ToObject<TransactionMetadata>(d[UserMetadata].AsDocument)
                            : null;

                        return (tx, metadata);
                    })
                    .ToList();

                return Task.FromResult<IEnumerable<(TezosTokenTransfer, TransactionMetadata)>>(transfers);
            }
            catch (Exception e)
            {
                Log.Error(e, "Error getting tezos tokens transfers");
            }

            return Task.FromResult(Enumerable.Empty<(TezosTokenTransfer, TransactionMetadata)>());
        }

        public Task<int> UpsertTokenContractsAsync(
            IEnumerable<TokenContract> tokenContracts,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var documents = tokenContracts
                    .Select(t => _bsonMapper.ToDocument(t));

                var upserted = _db
                    .GetCollection(TezosTokensContracts)
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
                    .GetCollection(TezosTokensContracts)
                    .FindAll()
                    .Select(d => new TokenContract
                    {
                        Address = d[TokenContractAddressKey].AsString,
                        Name = d[TokenContractNameKey].AsString,
                        Type = d.ContainsKey(TokenContractTypeKey)
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
                    return "FA2";

                if (contractTags.Contains("fa1-2"))
                    return "FA12";
            }

            var interfaces = d.ContainsKey("Interfaces")
                ? d["Interfaces"].AsArray
                    .Select(v => v.AsString)
                    .ToList()
                : null;

            if (interfaces == null)
                return "FA2";

            if (interfaces.FirstOrDefault(i => i == "TZIP-12" || i == "TZIP-012" || i.StartsWith("TZIP-012")) != null)
                return "FA2";

            if (interfaces.FirstOrDefault(i => i == "TZIP-7" || i == "TZIP-007" || i.StartsWith("TZIP-007")) != null)
                return "FA12";

            return "FA2";
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
                var transactions = _db
                    .GetCollection(TransactionCollectionName);

                transactions.EnsureIndex(CurrencyKey);

                var document = _bsonMapper.ToDocument(tx);

                document[UserMetadata] = new BsonDocument
                {
                    ["$id"] = document["_id"],
                    ["$ref"] = TransactionMetadataCollectionName
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
                var transactions = _db
                    .GetCollection(TransactionCollectionName);

                transactions.EnsureIndex(CurrencyKey);

                var documents = txs.Select(tx =>
                {
                    var d = _bsonMapper.ToDocument(tx);

                    d[UserMetadata] = new BsonDocument
                    {
                        ["$id"] = d["_id"],
                        ["$ref"] = TransactionMetadataCollectionName
                    };

                    return d;
                });

                var result = transactions.Upsert(documents);

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
                var document = _db
                    .GetCollection(TransactionCollectionName)
                    .FindById($"{txId}:{currency}");

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

        public async Task<(T,M)> GetTransactionWithMetadataByIdAsync<T,M>(
            string currency,
            string txId,
            CancellationToken cancellationToken = default)
            where T : ITransaction
            where M : ITransactionMetadata
        {
            var (tx, metadata) = await GetTransactionWithMetadataByIdAsync(currency, txId, typeof(T), typeof(M),cancellationToken)
                .ConfigureAwait(false);

            return ((T)tx, (M)metadata);
        }

        public Task<(ITransaction, ITransactionMetadata)> GetTransactionWithMetadataByIdAsync(
            string currency,
            string txId,
            Type transactionType,
            Type metadataType,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var document = _db
                    .GetCollection(TransactionCollectionName)
                    .Include(UserMetadata)
                    .FindById($"{txId}:{currency}");

                if (document != null)
                {
                    var tx = (ITransaction)_bsonMapper.ToObject(transactionType, document);
                    var metadata = !document[UserMetadata].AsDocument.ContainsKey("$missing")
                        ? (ITransactionMetadata)_bsonMapper.ToObject(metadataType, document[UserMetadata].AsDocument)
                        : null;

                    return Task.FromResult((tx, metadata));
                }
            }
            catch (Exception e)
            {
                Log.Error(e, "Error getting transaction by id");
            }

            return Task.FromResult<(ITransaction, ITransactionMetadata)>(default);
        }

        public Task<IEnumerable<ITransaction>> GetTransactionsAsync(
            string currency,
            Type transactionType,
            int offset = 0,
            int limit = int.MaxValue,
            SortDirection sort = SortDirection.Desc,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var transactions = _db
                    .GetCollection(TransactionCollectionName)
                    .Query()
                    .Where($"Currency = @0", currency)
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
            int offset = 0,
            int limit = int.MaxValue,
            SortDirection sort = SortDirection.Desc,
            CancellationToken cancellationToken = default)
            where T : ITransaction
        {
            try
            {
                var transactions = _db
                    .GetCollection(TransactionCollectionName)
                    .Query()
                    .Where($"Currency = @0", currency)
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

        public Task<IEnumerable<(ITransaction, ITransactionMetadata)>> GetTransactionsWithMetadataAsync(
            string currency,
            Type transactionType,
            Type metadataType,
            int offset = 0,
            int limit = int.MaxValue,
            SortDirection sort = SortDirection.Desc,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var transactions = _db
                    .GetCollection(TransactionCollectionName)
                    .Query()
                    .Include(UserMetadata)
                    .Where("Currency = @0", currency)
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

                        return (tx, metadata);
                    })
                    .ToList();

                return Task.FromResult<IEnumerable<(ITransaction, ITransactionMetadata)>>(transactions);
            }
            catch (Exception e)
            {
                Log.Error(e, $"Error getting transactions with metadata for {currency}");
            }

            return Task.FromResult(Enumerable.Empty<(ITransaction, ITransactionMetadata)>());
        }

        public Task<IEnumerable<(T,M)>> GetTransactionsWithMetadataAsync<T,M>(
            string currency,
            int offset = 0,
            int limit = int.MaxValue,
            SortDirection sort = SortDirection.Desc,
            CancellationToken cancellationToken = default)
            where T : ITransaction
            where M : ITransactionMetadata
        {
            try
            {
                var transactions = _db
                    .GetCollection(TransactionCollectionName)
                    .Query()
                    .Include(UserMetadata)
                    .Where($"Currency = @0", currency)
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

                        return (tx, metadata);
                    })
                    .ToList();

                return Task.FromResult<IEnumerable<(T,M)>>(transactions);
            }
            catch (Exception e)
            {
                Log.Error(e, $"Error getting transactions with metadata for {currency}");
            }

            return Task.FromResult(Enumerable.Empty<(T,M)>());
        }

        public Task<IEnumerable<T>> GetUnconfirmedTransactionsAsync<T>(
            string currency,
            CancellationToken cancellationToken = default) where T : ITransaction
        {
            try
            {
                var documents = _db
                    .GetCollection(TransactionCollectionName)
                    .Query()
                    .Where($"Currency = @0 AND Confirmations = 0", currency)
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
            CancellationToken cancellationToken = default)
        {
            try
            {
                var transactions = _db
                    .GetCollection(TransactionCollectionName);

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
                var transactionTypes = _db
                    .GetCollection(TransactionMetadataCollectionName);

                transactionTypes.EnsureIndex(CurrencyKey);

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
                var document = _db
                    .GetCollection(TransactionMetadataCollectionName)
                    .FindById($"{txId}:{currency}");

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
                var deleted = _db
                    .GetCollection(TransactionMetadataCollectionName)
                    .DeleteMany("Currency = @0", currency);

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
                var documents = outputs
                    .Select(o =>
                    {
                        var document = _bsonMapper.ToDocument(o);
                        document[AddressKey] = o.DestinationAddress(network);
                        return document;
                    });

                var outputsCollection = _db.GetCollection(OutputsCollectionName);
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
                var outputs = _db
                    .GetCollection(OutputsCollectionName)
                    .Query()
                    .Where("Currency = @0 AND SpentTxPoints = null", currency)
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
                var outputs = _db
                    .GetCollection(OutputsCollectionName)
                    .Query()
                    .Where("Currency = @0 AND Address = @1 AND SpentTxPoints = null", currency, address)
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
                var outputs = _db
                    .GetCollection(OutputsCollectionName)
                    .Query()
                    .Where("Currency = @0", currency)
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
            string address,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var outputs = _db
                    .GetCollection(OutputsCollectionName)
                    .Query()
                    .Where("Currency = @0 AND Address = @1", currency, address)
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
                var outputDocument = _db
                    .GetCollection(OutputsCollectionName)
                    .FindById($"{txId}:{index}:{currency}");

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
                Log.Error(e, "Error getting order");

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
                Log.Error(e, "Error getting order");

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
                Log.Error(e, "Error while remove order");

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
            CancellationToken cancellationToken = default)
        {
            try
            {
                var swaps = _db
                    .GetCollection<Swap>(SwapsCollectionName)
                    .Find(Query.All(), offset, limit)
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