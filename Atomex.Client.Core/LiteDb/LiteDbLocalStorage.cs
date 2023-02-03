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
using Atomex.Common.Bson;
using Atomex.Core;
using Atomex.Wallet.Abstract;
using Atomex.Wallet;

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

        private const string IdKey = "_id";
        private const string CurrencyKey = nameof(WalletAddress.Currency);
        private const string AddressKey = nameof(WalletAddress.Address);
        private const string BalanceKey = nameof(WalletAddress.Balance);
        private const string UnconfirmedIncomeKey = nameof(WalletAddress.UnconfirmedIncome);
        private const string UnconfirmedOutcomeKey = nameof(WalletAddress.UnconfirmedOutcome);
        private const string ChainKey = nameof(KeyIndex) + "." + nameof(KeyIndex.Chain);
        private const string IndexKey = nameof(KeyIndex) + "." + nameof(KeyIndex.Index);
        private const string HasActivityKey = nameof(WalletAddress.HasActivity);
        private const string TokenContractKey = nameof(TokenBalance) + "." + nameof(TokenBalance.Contract);
        private const string TokenIdKey = nameof(TokenBalance) + "." + nameof(TokenBalance.TokenId);
        private const string TransferContract = nameof(TezosTokenTransfer.Contract);
        private const string KeyTypeKey = nameof(WalletAddress.KeyType);

        public event EventHandler<BalanceChangedEventArgs> BalanceChanged;
        public event EventHandler<TransactionsChangedEventArgs> TransactionsChanged;

        private readonly string _pathToDb;
        private string _sessionPassword;
        private readonly BsonMapper _bsonMapper;
        private readonly LiteDatabase _db;

        public LiteDbLocalStorage(
            string pathToDb,
            SecureString password)
        {
            _pathToDb = pathToDb ??
                throw new ArgumentNullException(nameof(pathToDb));

            if (password == null)
                throw new ArgumentNullException(nameof(password));

            _sessionPassword = SessionPasswordHelper.GetSessionPassword(password);
            _bsonMapper = CreateBsonMapper();

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

        private BsonMapper CreateBsonMapper()
        {
            return new BsonMapper()
                .UseSerializer(new BigIntegerToBsonSerializer())
                .UseSerializer(new JObjectToBsonSerializer())
                .UseSerializer(new CoinToBsonSerializer());
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
                var addresses = _db.GetCollection(AddressesCollectionName);

                var addressId = WalletAddress.GetId(address, currency);
                var document = addresses.FindById(addressId);

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
            uint chain,
            int keyType,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var addresses = _db.GetCollection(AddressesCollectionName);

                var document = addresses
                    .Find(Query.And(
                        Query.EQ(CurrencyKey, currency),
                        Query.EQ(KeyTypeKey, keyType),
                        Query.EQ(ChainKey, (int)chain),
                        Query.EQ(HasActivityKey, true)))
                    .MaxByOrDefault(d => d["KeyIndex"]["Index"].AsInt32);

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

        public Task<WalletAddress> GetLastActiveWalletAddressByAccountAsync(
            string currency,
            int keyType)
        {
            try
            {
                var document = _db
                    .GetCollection(AddressesCollectionName)
                    .Find(Query.And(
                        Query.EQ(CurrencyKey, currency),
                        Query.EQ(KeyTypeKey, keyType),
                        Query.EQ(HasActivityKey, true)))
                    .MaxByOrDefault(d => d["KeyIndex"]["Account"].AsInt32);

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
            bool includeUnconfirmed = true)
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
            string currency)
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
            string address)
        {
            try
            {
                var document = _db
                    .GetCollection(TezosTokensAddresses)
                    .FindById(WalletAddress.GetId(address, currency, tokenContract, tokenId));

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

        public Task<IEnumerable<WalletAddress>> GetTokenAddressesAsync()
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
            string tokenContract)
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
            string tokenContract)
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
            IEnumerable<WalletAddress> walletAddresses)
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
            decimal tokenId)
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
            IEnumerable<TezosTokenTransfer> tokenTransfers)
        {
            try
            {
                var documents = tokenTransfers
                    .Select(t => _bsonMapper.ToDocument(t));

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
            int limit = int.MaxValue)
        {
            try
            {
                var transfers = _db
                    .GetCollection(TezosTokensTransfers)
                    .Find(Query.EQ(TransferContract, contractAddress), skip: offset, limit: limit)
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

        public Task<int> UpsertTokenContractsAsync(
            IEnumerable<TokenContract> tokenContracts)
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

        public Task<IEnumerable<TokenContract>> GetTokenContractsAsync()
        {
            try
            {
                var contracts = _db
                    .GetCollection(TezosTokensContracts)
                    .FindAll()
                    .Select(d => new TokenContract
                    {
                        Address = d["Address"].AsString,
                        Name = d["Name"].AsString,
                        Type = d.ContainsKey("Type")
                            ? d["Type"].AsString
                            : GetContractType(d),
                    }) // _bsonMapper.ToObject<TokenContract>(d))
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
                var transactions = _db.GetCollection(TransactionCollectionName);
                transactions.EnsureIndex(CurrencyKey);

                var upsertResult = transactions.Upsert(_bsonMapper.ToDocument(tx));

                // todo: notify if add or changed

                return Task.FromResult(upsertResult);
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
                var transactions = _db.GetCollection(TransactionCollectionName);
                transactions.EnsureIndex(CurrencyKey);

                var upsertResult = transactions.Upsert(txs.Select(tx => _bsonMapper.ToDocument(tx)));

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

        public Task<IEnumerable<ITransaction>> GetTransactionsAsync(
            string currency,
            Type transactionType,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var transactions = _db
                    .GetCollection(TransactionCollectionName)
                    .Find(Query.EQ(CurrencyKey, currency))
                    .Select(d => (ITransaction)_bsonMapper.ToObject(transactionType, d))
                    .ToList();

                return Task.FromResult<IEnumerable<ITransaction>>(transactions);
            }
            catch (Exception e)
            {
                Log.Error(e, "Error getting transactions");
            }

            return Task.FromResult(Enumerable.Empty<ITransaction>());
        }

        public Task<IEnumerable<T>> GetTransactionsAsync<T>(
            string currency,
            CancellationToken cancellationToken = default)
             where T : ITransaction
        {
            try
            {
                var transactions = _db
                    .GetCollection(TransactionCollectionName)
                    .Find(Query.EQ(CurrencyKey, currency))
                    .Select(_bsonMapper.ToObject<T>)
                    .ToList();

                return Task.FromResult<IEnumerable<T>>(transactions);
            }
            catch (Exception e)
            {
                Log.Error(e, "Error getting transactions");
            }

            return Task.FromResult(Enumerable.Empty<T>());
        }

        public async Task<IEnumerable<T>> GetUnconfirmedTransactionsAsync<T>(
            string currency,
            CancellationToken cancellationToken = default) where T : ITransaction
        {
            var transactions = await GetTransactionsAsync<T>(currency)
                .ConfigureAwait(false);

            return transactions.Where(t => !t.IsConfirmed);
        }

        public Task<bool> RemoveTransactionByIdAsync(
            string id,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var transactions = _db.GetCollection(TransactionCollectionName);
                return Task.FromResult(transactions.Delete(id));
            }
            catch (Exception e)
            {
                Log.Error(e, "Error removing transaction");
            }

            return Task.FromResult(false);
        }

        public Task<bool> UpsertTransactionsMetadataAsync(
            IEnumerable<ITransactionMetadata> types,
            bool notifyIfNewOrChanged = false,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var transactionTypes = _db.GetCollection(TransactionMetadataCollectionName);
                transactionTypes.EnsureIndex(CurrencyKey);

                var upsertResult = transactionTypes.Upsert(types.Select(t => _bsonMapper.ToDocument(t)));

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
                Log.Error(e, "Error getting transaction type by id");
            }

            return Task.FromResult<ITransactionMetadata>(default);
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
                        document[CurrencyKey] = currency;
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

        public async Task<IEnumerable<BitcoinTxOutput>> GetAvailableOutputsAsync(
            string currency,
            CancellationToken cancellationToken = default)
        {
            return (await GetOutputsAsync(currency)
                .ConfigureAwait(false))
                .Where(o => !o.IsSpent)
                .ToList();
        }

        public async Task<IEnumerable<BitcoinTxOutput>> GetAvailableOutputsAsync(
            string currency,
            string address,
            CancellationToken cancellationToken = default)
        {
            return (await GetOutputsAsync(currency, address, cancellationToken)
                .ConfigureAwait(false))
                .Where(o => !o.IsSpent)
                .ToList();
        }

        public Task<IEnumerable<BitcoinTxOutput>> GetOutputsAsync(
            string currency,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var outputs = _db.GetCollection(OutputsCollectionName)
                    .Find(Query.EQ(CurrencyKey, currency))
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
                    .Find(Query.And(
                        left: Query.EQ(CurrencyKey, currency),
                        right: Query.EQ(AddressKey, address)))
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
                    .Find(Query.And(
                        Query.EQ(CurrencyKey, currency),
                        Query.EQ("Coin.OutputHash", txId),
                        Query.EQ("Coin.OutputIndex", (int)index)))
                    .FirstOrDefault();

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

                var orders = _db.GetCollection(OrdersCollectionName);
                orders.Upsert(document);

                return Task.FromResult(true);
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
                var orders = _db.GetCollection(OrdersCollectionName);

                var documents = orders.Find(Query.EQ("OrderId", id));

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
                    .DeleteMany(Query.EQ("OrderId", id));

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
                    .FindOne(Query.EQ("PaymentTxId", txId));

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