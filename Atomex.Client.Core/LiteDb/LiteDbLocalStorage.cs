using System;
using System.Collections.Generic;
using System.Linq;
using System.Security;
using System.Threading;
using System.Threading.Tasks;

using LiteDB;
using Serilog;

using Atomex.Abstract;
using Atomex.Blockchain.Abstract;
using Atomex.Blockchain.BitcoinBased;
using Atomex.Blockchain.Tezos;
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
        private const string TransferContract = nameof(TokenTransfer.Contract);
        private const string KeyTypeKey = nameof(WalletAddress.KeyType);

        public event EventHandler<BalanceChangedEventArgs> BalanceChanged;
        public event EventHandler<TransactionsChangedEventArgs> TransactionsChanged;

        private readonly string _pathToDb;
        private string _sessionPassword;
        private readonly BsonMapper _bsonMapper;
        private readonly object _syncRoot = new();

        private string ConnectionString => $"FileName={_pathToDb};Password={_sessionPassword};Mode=Exclusive";

        public LiteDbLocalStorage(
            string pathToDb,
            SecureString password,
            ICurrencies currencies,
            Network network,
            Action<MigrationActionType> migrationComplete = null)
        {
            _pathToDb = pathToDb ??
                throw new ArgumentNullException(nameof(pathToDb));

            if (password == null)
                throw new ArgumentNullException(nameof(password));

            if (currencies == null)
                throw new ArgumentNullException(nameof(currencies));

            _sessionPassword = SessionPasswordHelper.GetSessionPassword(password);
            _bsonMapper = CreateBsonMapper(currencies);

            LiteDbMigrationManager.Migrate(
                pathToDb: _pathToDb,
                sessionPassword: _sessionPassword,
                network: network,
                migrationComplete);
        }

        public void ChangePassword(SecureString newPassword)
        {
            var newSessionPassword = SessionPasswordHelper.GetSessionPassword(newPassword);

            using var db = new LiteDatabase(ConnectionString, _bsonMapper);

            db.Shrink(newSessionPassword);

            _sessionPassword = newSessionPassword;
        }

        private BsonMapper CreateBsonMapper(ICurrencies currencies)
        {
            return new BsonMapper()
                .UseSerializer(new CurrencyToBsonSerializer(currencies))
                .UseSerializer(new BigIntegerToBsonSerializer())
                .UseSerializer(new JObjectToBsonSerializer())
                .UseSerializer(new WalletAddressToBsonSerializer())
                .UseSerializer(new OrderToBsonSerializer())
                .UseSerializer(new BitcoinBasedTransactionToBsonSerializer(currencies))
                .UseSerializer(new BitcoinBasedTxOutputToBsonSerializer())
                .UseSerializer(new EthereumTransactionToBsonSerializer())
                .UseSerializer(new TezosTransactionToBsonSerializer())
                .UseSerializer(new SwapToBsonSerializer(currencies));
        }

        #region Addresses

        public Task<bool> UpsertAddressAsync(
            WalletAddress walletAddress)
        {
            try
            {
                lock (_syncRoot)
                {
                    using var db = new LiteDatabase(ConnectionString, _bsonMapper);

                    var document = _bsonMapper.ToDocument(walletAddress);

                    var addresses = db.GetCollection(AddressesCollectionName);
                    addresses.EnsureIndex(IndexKey);
                    addresses.EnsureIndex(CurrencyKey);
                    addresses.EnsureIndex(AddressKey);
                    var result = addresses.Upsert(document);

                    return Task.FromResult(result);
                }
            }
            catch (Exception e)
            {
                Log.Error(e, "Error updating address");
            }

            return Task.FromResult(false);
        }

        public Task<int> UpsertAddressesAsync(
            IEnumerable<WalletAddress> walletAddresses)
        {
            try
            {
                lock (_syncRoot)
                {
                    using var db = new LiteDatabase(ConnectionString, _bsonMapper);

                    var documents = walletAddresses.Select(_bsonMapper.ToDocument);

                    var addresses = db.GetCollection(AddressesCollectionName);
                    addresses.EnsureIndex(IndexKey);
                    addresses.EnsureIndex(CurrencyKey);
                    addresses.EnsureIndex(AddressKey);
                    var result = addresses.Upsert(documents);

                    return Task.FromResult(result);
                }
            }
            catch (Exception e)
            {
                Log.Error(e, "Error updating address");
            }

            return Task.FromResult(0);
        }

        public Task<WalletAddress> GetWalletAddressAsync(
            string currency,
            string address)
        {
            try
            {
                lock (_syncRoot)
                {
                    using var db = new LiteDatabase(ConnectionString, _bsonMapper);
                    var addresses = db.GetCollection(AddressesCollectionName);

                    var document = addresses.FindById($"{address}:{currency}");

                    var walletAddress = document != null
                        ? _bsonMapper.ToObject<WalletAddress>(document)
                        : null;

                    return Task.FromResult(walletAddress);
                }
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
            int keyType)
        {
            try
            {
                lock (_syncRoot)
                {
                    using var db = new LiteDatabase(ConnectionString, _bsonMapper);
                    var addresses = db.GetCollection(AddressesCollectionName);

                    var document = addresses.FindOne(
                        Query.And(
                            Query.All(IndexKey, Query.Descending),
                            Query.EQ(CurrencyKey, currency),
                            Query.EQ(ChainKey, (int)chain),
                            Query.EQ(KeyTypeKey, keyType),
                            Query.EQ(HasActivityKey, true)));

                    var walletAddress = document != null
                        ? _bsonMapper.ToObject<WalletAddress>(document)
                        : null;

                    return Task.FromResult(walletAddress);
                }
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
                lock (_syncRoot)
                {
                    using var db = new LiteDatabase(ConnectionString, _bsonMapper);
                    var addresses = db.GetCollection(AddressesCollectionName);

                    var documents = addresses.Find(Query.And(
                        Query.EQ(CurrencyKey, currency),
                        Query.EQ(KeyTypeKey, keyType),
                        Query.EQ(HasActivityKey, true)));

                    var document = documents
                        .OrderByDescending(d => d["KeyIndex"].AsDocument["Account"].AsInt32)
                        .FirstOrDefault();

                    var walletAddress = document != null
                        ? _bsonMapper.ToObject<WalletAddress>(document)
                        : null;

                    return Task.FromResult(walletAddress);
                }
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
                lock (_syncRoot)
                {
                    using var db = new LiteDatabase(ConnectionString, _bsonMapper);
                    var addresses = db.GetCollection(AddressesCollectionName);

                    var unspentAddresses = addresses
                        .Find(query)
                        .Select(d => _bsonMapper.ToObject<WalletAddress>(d))
                        .ToList();

                    return Task.FromResult<IEnumerable<WalletAddress>>(unspentAddresses);
                }
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
                lock (_syncRoot)
                {
                    using var db = new LiteDatabase(ConnectionString, _bsonMapper);
                    var addresses = db.GetCollection(AddressesCollectionName);

                    var unspentAddresses = addresses
                        .Find(Query.EQ(CurrencyKey, currency))
                        .Select(d => _bsonMapper.ToObject<WalletAddress>(d))
                        .ToList();

                    return Task.FromResult<IEnumerable<WalletAddress>>(unspentAddresses);
                }
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
            decimal tokenId,
            string address)
        {
            try
            {
                lock (_syncRoot)
                {
                    using var db = new LiteDatabase(ConnectionString, _bsonMapper);
                    var addresses = db.GetCollection(TezosTokensAddresses);

                    var document = addresses.FindById($"{address}:{currency}:{tokenContract}:{tokenId}");

                    var walletAddress = document != null
                        ? _bsonMapper.ToObject<WalletAddress>(document)
                        : null;

                    return Task.FromResult(walletAddress);
                }
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
                lock (_syncRoot)
                {
                    using var db = new LiteDatabase(ConnectionString, _bsonMapper);
                    var tezosTokenAddresses = db.GetCollection(TezosTokensAddresses);

                    var addresses = tezosTokenAddresses
                        .FindAll()
                        .Select(d => _bsonMapper.ToObject<WalletAddress>(d))
                        .ToList();

                    return Task.FromResult<IEnumerable<WalletAddress>>(addresses);
                }
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
                lock (_syncRoot)
                {
                    using var db = new LiteDatabase(ConnectionString, _bsonMapper);
                    var tezosTokenAddresses = db.GetCollection(TezosTokensAddresses);

                    var addresses = tezosTokenAddresses
                        .Find(Query.And(
                            Query.EQ(AddressKey, address),
                            Query.EQ(TokenContractKey, tokenContract)))
                        .Select(d => _bsonMapper.ToObject<WalletAddress>(d))
                        .ToList();

                    return Task.FromResult<IEnumerable<WalletAddress>>(addresses);
                }
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
                lock (_syncRoot)
                {
                    using var db = new LiteDatabase(ConnectionString, _bsonMapper);
                    var tezosTokenAddresses = db.GetCollection(TezosTokensAddresses);

                    var addresses = tezosTokenAddresses
                        .Find(Query.EQ(TokenContractKey, tokenContract))
                        .Select(d => _bsonMapper.ToObject<WalletAddress>(d))
                        .ToList();

                    return Task.FromResult<IEnumerable<WalletAddress>>(addresses);
                }
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
                lock (_syncRoot)
                {
                    using var db = new LiteDatabase(ConnectionString, _bsonMapper);

                    var documents = walletAddresses.Select(_bsonMapper.ToDocument);

                    var addresses = db.GetCollection(TezosTokensAddresses);
                    //addresses.EnsureIndex(IndexKey);
                    //addresses.EnsureIndex(CurrencyKey);
                    //addresses.EnsureIndex(AddressKey);

                    var result = addresses.Upsert(documents);

                    return Task.FromResult(result);
                }
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
            var queries = new List<Query>
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
                lock (_syncRoot)
                {
                    using var db = new LiteDatabase(ConnectionString, _bsonMapper);
                    var addresses = db.GetCollection(TezosTokensAddresses);

                    var unspentAddresses = addresses
                        .Find(query)
                        .Select(d => _bsonMapper.ToObject<WalletAddress>(d))
                        .ToList();

                    return Task.FromResult<IEnumerable<WalletAddress>>(unspentAddresses);
                }
            }
            catch (Exception e)
            {
                Log.Error(e, "Error getting unspent tezos tokens wallet addresses");
            }

            return Task.FromResult(Enumerable.Empty<WalletAddress>());
        }

        public Task<int> UpsertTokenTransfersAsync(
            IEnumerable<TokenTransfer> tokenTransfers)
        {
            try
            {
                lock (_syncRoot)
                {
                    using var db = new LiteDatabase(ConnectionString, _bsonMapper);

                    var transfers = db.GetCollection(TezosTokensTransfers);

                    var documents = tokenTransfers
                        .Select(t => _bsonMapper.ToDocument(t));

                    var upserted = transfers.Upsert(documents);

                    return Task.FromResult(upserted);
                }
            }
            catch (Exception e)
            {
                Log.Error(e, "Error adding transfers");
            }

            return Task.FromResult(0);
        }

        public Task<IEnumerable<TokenTransfer>> GetTokenTransfersAsync(
            string contractAddress,
            int offset = 0,
            int limit = int.MaxValue)
        {
            try
            {
                lock (_syncRoot)
                {
                    using var db = new LiteDatabase(ConnectionString, _bsonMapper);

                    var transfers = db.GetCollection(TezosTokensTransfers)
                        .Find(Query.EQ(TransferContract, contractAddress), skip: offset, limit: limit)
                        .Select(d => _bsonMapper.ToObject<TokenTransfer>(d))
                        .ToList();

                    return Task.FromResult<IEnumerable<TokenTransfer>>(transfers);
                }
            }
            catch (Exception e)
            {
                Log.Error(e, "Error getting tezos tokens transfers");
            }

            return Task.FromResult(Enumerable.Empty<TokenTransfer>());
        }

        public Task<int> UpsertTokenContractsAsync(
            IEnumerable<TokenContract> tokenContracts)
        {
            try
            {
                lock (_syncRoot)
                {
                    using var db = new LiteDatabase(ConnectionString, _bsonMapper);

                    var contracts = db.GetCollection(TezosTokensContracts);

                    var documents = tokenContracts
                        .Select(t => _bsonMapper.ToDocument(t));

                    var upserted = contracts.Upsert(documents);

                    return Task.FromResult(upserted);
                }
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
                lock (_syncRoot)
                {
                    using var db = new LiteDatabase(ConnectionString, _bsonMapper);
                    var contractsCollection = db.GetCollection(TezosTokensContracts);

                    var contracts = contractsCollection
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
            IBlockchainTransaction tx,
            bool notifyIfNewOrChanged = false,
            CancellationToken cancellationToken = default)
        {
            try
            {
                lock (_syncRoot)
                {
                    using var db = new LiteDatabase(ConnectionString, _bsonMapper);

                    var transactions = db.GetCollection(TransactionCollectionName);
                    transactions.EnsureIndex(CurrencyKey);

                    var upsertResult = transactions.Upsert(_bsonMapper.ToDocument(tx));

                    // todo: notify if add or changed

                    return Task.FromResult(upsertResult);
                }
            }
            catch (Exception e)
            {
                Log.Error(e, "Error adding transaction");
            }

            return Task.FromResult(false);
        }

        public Task<bool> UpsertTransactionsAsync(
            IEnumerable<IBlockchainTransaction> txs,
            bool notifyIfNewOrChanged = false,
            CancellationToken cancellationToken = default)
        {
            try
            {
                lock (_syncRoot)
                {
                    using var db = new LiteDatabase(ConnectionString, _bsonMapper);

                    var transactions = db.GetCollection(TransactionCollectionName);
                    transactions.EnsureIndex(CurrencyKey);

                    var upsertResult = transactions.Upsert(txs.Select(tx => _bsonMapper.ToDocument(tx)));

                    // todo: notify if add or changed

                    return Task.FromResult(true);
                }
            }
            catch (Exception e)
            {
                Log.Error(e, "Error adding transactions");
            }

            return Task.FromResult(false);
        }

        public Task<T> GetTransactionByIdAsync<T>(
            string currency,
            string txId) where T : IBlockchainTransaction
        {
            try
            {
                lock (_syncRoot)
                {
                    using var db = new LiteDatabase(ConnectionString, _bsonMapper);
                    var document = db.GetCollection(TransactionCollectionName)
                        .FindById($"{txId}:{currency}");

                    if (document != null)
                    {
                        var tx = _bsonMapper.ToObject<T>(document);

                        return Task.FromResult(tx);
                    }
                }
            }
            catch (Exception e)
            {
                Log.Error(e, "Error getting transaction by id");
            }

            return Task.FromResult<T>(default);
        }

        public Task<IEnumerable<IBlockchainTransaction>> GetTransactionsAsync(
            string currency,
            Type transactionType)
        {
            try
            {
                lock (_syncRoot)
                {
                    using var db = new LiteDatabase(ConnectionString, _bsonMapper);

                    var transactions = db.GetCollection(TransactionCollectionName)
                        .Find(Query.EQ(CurrencyKey, currency))
                        .Select(d => (IBlockchainTransaction)_bsonMapper.ToObject(transactionType, d))
                        .ToList();

                    return Task.FromResult<IEnumerable<IBlockchainTransaction>>(transactions);
                }
            }
            catch (Exception e)
            {
                Log.Error(e, "Error getting transactions");
            }

            return Task.FromResult(Enumerable.Empty<IBlockchainTransaction>());
        }

        public Task<IEnumerable<T>> GetTransactionsAsync<T>(string currency)
             where T : IBlockchainTransaction
        {
            try
            {
                lock (_syncRoot)
                {
                    using var db = new LiteDatabase(ConnectionString, _bsonMapper);

                    var transactions = db.GetCollection(TransactionCollectionName)
                        .Find(Query.EQ(CurrencyKey, currency))
                        .Select(d => _bsonMapper.ToObject<T>(d))
                        .ToList();

                    return Task.FromResult<IEnumerable<T>>(transactions);
                }
            }
            catch (Exception e)
            {
                Log.Error(e, "Error getting transactions");
            }

            return Task.FromResult(Enumerable.Empty<T>());
        }

        public async Task<IEnumerable<T>> GetUnconfirmedTransactionsAsync<T>(
            string currency) where T : IBlockchainTransaction
        {
            var transactions = await GetTransactionsAsync<T>(currency)
                .ConfigureAwait(false);

            return transactions.Where(t => !t.IsConfirmed);
        }

        public Task<bool> RemoveTransactionByIdAsync(string id)
        {
            try
            {
                lock (_syncRoot)
                {
                    using var db = new LiteDatabase(ConnectionString, _bsonMapper);
                    var transactions = db.GetCollection(TransactionCollectionName);
                    return Task.FromResult(transactions.Delete(id));
                }
            }
            catch (Exception e)
            {
                Log.Error(e, "Error removing transaction");
            }

            return Task.FromResult(false);
        }

        #endregion Transactions

        #region Outputs

        public Task<bool> UpsertOutputsAsync(
            IEnumerable<BitcoinBasedTxOutput> outputs,
            string currency,
            NBitcoin.Network network)
        {
            try
            {
                lock (_syncRoot)
                {
                    using var db = new LiteDatabase(ConnectionString, _bsonMapper);

                    var documents = outputs
                        .Select(o =>
                        {
                            var document = _bsonMapper.ToDocument(o);
                            document[CurrencyKey] = currency;
                            document[AddressKey] = o.DestinationAddress(network);
                            return document;
                        });

                    var outputsCollection = db.GetCollection(OutputsCollectionName);
                    outputsCollection.EnsureIndex(CurrencyKey);
                    outputsCollection.Upsert(documents);

                    return Task.FromResult(true);
                }
            }
            catch (Exception e)
            {
                Log.Error(e, "Error while upserting outputs");
            }

            return Task.FromResult(false);
        }

        public async Task<IEnumerable<BitcoinBasedTxOutput>> GetAvailableOutputsAsync(
            string currency)
        {
            return (await GetOutputsAsync(currency)
                .ConfigureAwait(false))
                .Where(o => !o.IsSpent)
                .ToList();

            //return await GetOnlyConfirmedOutputsAsync(currency, outputs, transactionType)
            //    .ConfigureAwait(false);
        }

        public async Task<IEnumerable<BitcoinBasedTxOutput>> GetAvailableOutputsAsync(
            string currency,
            string address)
        {
            return (await GetOutputsAsync(currency, address)
                .ConfigureAwait(false))
                .Where(o => !o.IsSpent)
                .ToList();

            //return await GetOnlyConfirmedOutputsAsync(currency, outputs, transactionType)
            //    .ConfigureAwait(false);
        }

        //private async Task<IEnumerable<BitcoinBasedTxOutput>> GetOnlyConfirmedOutputsAsync(
        //    string currency,
        //    IEnumerable<BitcoinBasedTxOutput> outputs,
        //    Type transactionType)
        //{
        //    var confirmedOutputs = new List<BitcoinBasedTxOutput>();

        //    foreach (var o in outputs)
        //    {
        //        var tx = await GetTransactionByIdAsync(currency, o.TxId, transactionType)
        //            .ConfigureAwait(false);

        //        if (tx?.IsConfirmed ?? false)
        //            confirmedOutputs.Add(o);
        //    }

        //    return confirmedOutputs;
        //}

        public Task<IEnumerable<BitcoinBasedTxOutput>> GetOutputsAsync(string currency)
        {
            try
            {
                lock (_syncRoot)
                {
                    using var db = new LiteDatabase(ConnectionString, _bsonMapper);

                    var outputs = db.GetCollection(OutputsCollectionName)
                        .Find(Query.EQ(CurrencyKey, currency))
                        .Select(d => _bsonMapper.ToObject<BitcoinBasedTxOutput>(d))
                        .ToList();

                    return Task.FromResult<IEnumerable<BitcoinBasedTxOutput>>(outputs);
                }
            }
            catch (Exception e)
            {
                Log.Error(e, "Error getting outputs");
            }

            return Task.FromResult(Enumerable.Empty<BitcoinBasedTxOutput>());
        }

        public Task<IEnumerable<BitcoinBasedTxOutput>> GetOutputsAsync(
            string currency,
            string address)
        {
            try
            {
                lock (_syncRoot)
                {
                    using var db = new LiteDatabase(ConnectionString, _bsonMapper);

                    var outputs = db
                        .GetCollection(OutputsCollectionName)
                        .Find(Query.And(
                            left: Query.EQ(CurrencyKey, currency),
                            right: Query.EQ(AddressKey, address)))
                        .Select(d => _bsonMapper.ToObject<BitcoinBasedTxOutput>(d))
                        .ToList();

                    return Task.FromResult<IEnumerable<BitcoinBasedTxOutput>>(outputs);
                }
            }
            catch (Exception e)
            {
                Log.Error(e, "Error getting outputs");
            }

            return Task.FromResult(Enumerable.Empty<BitcoinBasedTxOutput>());
        }

        #endregion Outputs

        #region Orders

        public Task<bool> UpsertOrderAsync(Order order)
        {
            try
            {
                lock (_syncRoot)
                {
                    var localOrder = GetOrderByIdUnsync(order.ClientOrderId);

                    if (!order.VerifyOrder(localOrder))
                        return Task.FromResult(false);

                    if (localOrder != null)
                        order.ForwardLocalParameters(localOrder);

                    using var db = new LiteDatabase(ConnectionString, _bsonMapper);

                    var document = _bsonMapper.ToDocument(order);

                    var orders = db.GetCollection(OrdersCollectionName);
                    orders.Upsert(document);

                    return Task.FromResult(true);
                }
            }
            catch (Exception e)
            {
                Log.Error(e, "Error adding order");
            }

            return Task.FromResult(false);
        }

        public Task<bool> RemoveAllOrdersAsync()
        {
            try
            {
                lock (_syncRoot)
                {
                    using var db = new LiteDatabase(ConnectionString, _bsonMapper);

                    var collectionExists = db.DropCollection(OrdersCollectionName);

                    if (collectionExists)
                        Log.Debug("The {Collection} collection is dropped", OrdersCollectionName);
                    else
                        Log.Debug("The {Collection} collection does not exist. Nothing to drop", OrdersCollectionName);

                    return Task.FromResult(true);
                }
            }
            catch (Exception e)
            {
                Log.Error(e, "Dropping the {Collection} collection failed", OrdersCollectionName);
            }

            return Task.FromResult(false);
        }

        public Order GetOrderById(string clientOrderId)
        {
            lock (_syncRoot)
            {
                return GetOrderByIdUnsync(clientOrderId);
            }
        }

        private Order GetOrderByIdUnsync(string clientOrderId)
        {
            try
            {
                using var db = new LiteDatabase(ConnectionString, _bsonMapper);

                var orders = db.GetCollection(OrdersCollectionName);

                var document = orders.FindById(clientOrderId);

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

        public Order GetOrderById(long id)
        {
            try
            {
                lock (_syncRoot)
                {
                    using var db = new LiteDatabase(ConnectionString, _bsonMapper);

                    var orders = db.GetCollection(OrdersCollectionName);

                    var documents = orders.Find(Query.EQ("OrderId", id));

                    return documents != null && documents.Any()
                        ? _bsonMapper.ToObject<Order>(documents.First())
                        : null;
                }
            }
            catch (Exception e)
            {
                Log.Error(e, "Error getting order");

                return null;
            }
        }

        public Task<bool> RemoveOrderByIdAsync(long id)
        {
            try
            {
                lock (_syncRoot)
                {
                    using var db = new LiteDatabase(ConnectionString, _bsonMapper);

                    var orders = db.GetCollection(OrdersCollectionName);

                    var removed = orders.Delete(Query.EQ("OrderId", id));

                    return Task.FromResult(removed > 0);
                }
            }
            catch (Exception e)
            {
                Log.Error(e, "Error while remove order");

                return Task.FromResult(false);
            }
        }

        #endregion Orders

        #region Swaps

        public Task<bool> AddSwapAsync(Swap swap)
        {
            try
            {
                lock (_syncRoot)
                {
                    using var db = new LiteDatabase(ConnectionString, _bsonMapper);

                    db.GetCollection<Swap>(SwapsCollectionName)
                        .Insert(swap);

                    return Task.FromResult(true);
                }
            }
            catch (Exception e)
            {
                Log.Error(e, "Swap add error");
            }

            return Task.FromResult(false);
        }

        public Task<bool> UpdateSwapAsync(Swap swap)
        {
            try
            {
                lock (_syncRoot)
                {
                    using var db = new LiteDatabase(ConnectionString, _bsonMapper);

                    var result = db
                        .GetCollection<Swap>(SwapsCollectionName)
                        .Update(swap);

                    return Task.FromResult(result);
                }
            }
            catch (Exception e)
            {
                Log.Error(e, "Swap update error");
            }

            return Task.FromResult(false);
        }

        public Task<Swap> GetSwapByIdAsync(long id)
        {
            try
            {
                lock (_syncRoot)
                {
                    using var db = new LiteDatabase(ConnectionString, _bsonMapper);

                    var swap = db
                        .GetCollection<Swap>(SwapsCollectionName)
                        .FindById(id);

                    return Task.FromResult(swap);
                }
            }
            catch (Exception e)
            {
                Log.Error(e, "Error getting swap by id");
            }

            return Task.FromResult<Swap>(null);
        }

        public Task<IEnumerable<Swap>> GetSwapsAsync()
        {
            try
            {
                lock (_syncRoot)
                {
                    using var db = new LiteDatabase(ConnectionString, _bsonMapper);

                    var swaps = db
                        .GetCollection<Swap>(SwapsCollectionName)
                        .Find(Query.All())
                        .ToList();

                    return Task.FromResult<IEnumerable<Swap>>(swaps);
                }
            }
            catch (Exception e)
            {
                Log.Error(e, "Swaps getting error");
            }

            return Task.FromResult(Enumerable.Empty<Swap>());
        }

        #endregion Swaps
    }
}