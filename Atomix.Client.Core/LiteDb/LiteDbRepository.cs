using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Security;
using System.Threading.Tasks;
using Atomix.Blockchain.Abstract;
using Atomix.Blockchain.BitcoinBased;
using Atomix.Blockchain.Ethereum;
using Atomix.Blockchain.Tezos;
using Atomix.Common;
using Atomix.Common.Bson;
using Atomix.Core;
using Atomix.Core.Abstract;
using Atomix.Core.Entities;
using Atomix.Swaps;
using Atomix.Swaps.Abstract;
using LiteDB;
using Serilog;

namespace Atomix.LiteDb
{
    public class LiteDbRepository : IOrderRepository, ISwapRepository, ITransactionRepository
    {
        public const string OrdersCollectionName = "orders";
        public const string SwapsCollectionName = "swaps";
        public const string CurrencyKey = "currency";
        public const string AddressKey = "address";
        public const string TransactionCollectionName = "transactions";
        public const string OutputsCollectionName = "outputs";

        private readonly string _pathToDb;
        private readonly string _sessionPassword;

        private readonly ConcurrentDictionary<Guid, ISwapState> _swapById = new ConcurrentDictionary<Guid, ISwapState>();
        private bool _swapsLoaded;
        private readonly object _syncRoot = new object();

        protected string ConnectionString => $"FileName={_pathToDb};Password={_sessionPassword}";

        static LiteDbRepository()
        {
            new CurrencyToBsonSerializer().Register();
            new SymbolToBsonSerializer().Register();
            new BitcoinBasedTransactionToBsonSerializer().Register();
            new BitcoinBasedTxOutputToBsonSerializer().Register();
            new EthereumTransactionToBsonSerializer().Register();
            new TezosTransactionToBsonSerializer().Register();
            new SwapToBsonSerializer().Register();
        }

        public LiteDbRepository(string pathToDb, SecureString password)
        {
            _pathToDb = pathToDb ?? throw new ArgumentNullException(nameof(pathToDb));

            if (password == null)
                throw new ArgumentNullException(nameof(password));

            _sessionPassword = SessionPasswordHelper.GetSessionPassword(password);
        }

        #region Orders

        public Task<bool> AddOrderAsync(Order order)
        {
            try
            {
                lock (_syncRoot)
                {
                    if (!CheckOrder(order))
                        return Task.FromResult(false);

                    using (var db = new LiteDatabase(ConnectionString))
                    {
                        var orders = db.GetCollection<Order>(OrdersCollectionName);

                        orders.EnsureIndex(o => o.OrderId);
                        orders.EnsureIndex(o => o.ClientOrderId);
                        orders.Insert(order);
                    }

                    return Task.FromResult(true);
                }
            }
            catch (Exception e)
            {
                Log.Error(e, "Error adding order");
            }

            return Task.FromResult(false);
        }

        private IEnumerable<Order> GetPendingOrders(string clientOrderId)
        {
            try
            {
                lock (_syncRoot)
                {
                    using (var db = new LiteDatabase(ConnectionString))
                    {
                        var orders = db.GetCollection<Order>(OrdersCollectionName);

                        return orders
                            .Find(o => o.ClientOrderId == clientOrderId && o.OrderId == Guid.Empty)
                            .OrderByDescending(o => o.Id);
                    }
                }
            }
            catch (Exception e)
            {
                Log.Error(e, "Error getting pending orders");

                return Enumerable.Empty<Order>();
            }
        }

        private IEnumerable<Order> GetOrders(Guid orderId)
        {
            try
            {
                lock (_syncRoot)
                {
                    using (var db = new LiteDatabase(ConnectionString))
                    {
                        var orders = db.GetCollection<Order>(OrdersCollectionName);

                        return orders
                            .Find(o => o.OrderId == orderId)
                            .OrderByDescending(o => o.Id);
                    }
                }
            }
            catch (Exception e)
            {
                Log.Error(e, "Error getting orders");

                return Enumerable.Empty<Order>();
            }
        }

        private bool CheckOrder(Order order)
        {
            if (order.Status == OrderStatus.Unknown || order.Status == OrderStatus.Pending)
            {
                var pendingOrders = GetPendingOrders(order.ClientOrderId);

                if (pendingOrders.Any())
                {
                    Log.Error(messageTemplate: "Order already pending");

                    return false;
                }
            }
            else if (order.Status == OrderStatus.Placed || order.Status == OrderStatus.Rejected)
            {
                var pendingOrders = GetPendingOrders(order.ClientOrderId)
                    .ToList();

                if (!pendingOrders.Any())
                {
                    order.IsApproved = false;

                    // probably a different device order
                    Log.Information(
                        messageTemplate: "Probably order from another device: {@order}",
                        propertyValue: order.ToString());
                }
                else
                {
                    var pendingOrder = pendingOrders.First();

                    if (pendingOrder.Status == OrderStatus.Rejected)
                    {
                        Log.Error(messageTemplate: "Order already rejected");

                        return false;
                    }

                    if (!order.IsContinuationOf(pendingOrder))
                    {
                        Log.Error(
                            messageTemplate:
                            "Order is not continuation of saved pending order! Order: {@order}, pending order: {@pendingOrder}",
                            propertyValue0: order.ToString(),
                            propertyValue1: pendingOrder.ToString());

                        return false;
                    }
                }
            }
            else
            {
                var orders = GetOrders(order.OrderId)
                    .ToList();

                if (!orders.Any())
                {
                    Log.Error(
                        messageTemplate: "Order is not continuation of saved orders! Order: {@order}",
                        propertyValue: order.ToString());

                    return false;
                }

                var actualOrder = orders.First();

                if (!order.IsContinuationOf(actualOrder))
                {
                    Log.Error(
                        messageTemplate: "Order is not continuation of saved order! Order: {@order}, saved order: {@actualOrder}",
                        propertyValue0: order.ToString(),
                        propertyValue1: actualOrder.ToString());

                    return false;
                }
            }

            return true;
        }

        #endregion

        #region Swaps

        public Task<bool> AddSwapAsync(ISwapState swap)
        {
            if (!_swapById.TryAdd(swap.Id, swap))
                return Task.FromResult(false);

            try
            {
                lock (_syncRoot)
                {
                    using (var db = new LiteDatabase(ConnectionString))
                    {
                        var swaps = db.GetCollection<SwapState>(SwapsCollectionName);

                        swaps.Insert((SwapState) swap);
                    }

                    return Task.FromResult(true);
                }
            }
            catch (Exception e)
            {
                Log.Error(e, "Swap add error");
            }

            return Task.FromResult(false);
        }

        public Task<bool> UpdateSwapAsync(ISwapState swap)
        {
            try
            {
                lock (_syncRoot)
                {
                    using (var db = new LiteDatabase(ConnectionString))
                    {
                        var swaps = db.GetCollection<SwapState>(SwapsCollectionName);

                        return Task.FromResult(swaps.Update((SwapState) swap));
                    }
                }
            }
            catch (Exception e)
            {
                Log.Error(e, "Swap update error");
            }

            return Task.FromResult(false);
        }

        public Task<bool> RemoveSwapAsync(ISwapState swap)
        {
            if (!_swapById.TryRemove(swap.Id, out _))
                return Task.FromResult(false);

            try
            {
                lock (_syncRoot)
                {
                    using (var db = new LiteDatabase(ConnectionString))
                    {
                        var swaps = db.GetCollection<SwapState>(SwapsCollectionName);

                        return Task.FromResult(swaps.Delete(swap.Id));
                    }
                }
            }
            catch (Exception e)
            {
                Log.Error(e, "Swap removal error");
            }

            return Task.FromResult(false);
        }

        public Task<ISwapState> GetSwapByIdAsync(Guid id)
        {
            if (_swapById.TryGetValue(id, out var swap))
                return Task.FromResult(swap);

            try
            {
                lock (_syncRoot)
                {
                    using (var db = new LiteDatabase(ConnectionString))
                    {
                        var swapCollection = db.GetCollection<SwapState>(SwapsCollectionName);

                        swap = swapCollection.FindById(id);

                        if (swap != null)
                        {
                            _swapById.TryAdd(swap.Id, swap);
                            return Task.FromResult(swap);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Log.Error(e, "Error getting swap by id");
            }

            return Task.FromResult<ISwapState>(null);
        }

        public Task<IEnumerable<ISwapState>> GetSwapsAsync()
        {
            if (_swapsLoaded)
                return Task.FromResult<IEnumerable<ISwapState>>(_swapById.Values);

            try
            {
                lock (_syncRoot)
                {
                    using (var db = new LiteDatabase(ConnectionString))
                    {
                        var swapCollection = db.GetCollection<SwapState>(SwapsCollectionName);

                        var swaps = swapCollection.Find(Query.All());

                        foreach (var swap in swaps)
                            if (!_swapById.ContainsKey(swap.Id))
                                _swapById.TryAdd(swap.Id, swap);

                        _swapsLoaded = true;

                        return Task.FromResult<IEnumerable<ISwapState>>(_swapById.Values);
                    }
                }
            }
            catch (Exception e)
            {
                Log.Error(e, "Swaps getting error");
            }

            return Task.FromResult(Enumerable.Empty<ISwapState>());
        }

        #endregion

        #region Transactions

        public Task<bool> AddTransactionAsync(IBlockchainTransaction tx)
        {
            try
            {
                lock (_syncRoot)
                {
                    using (var db = new LiteDatabase(ConnectionString))
                    {
                        var transactions = db.GetCollection(TransactionCollectionName);

                        var document = BsonMapper.Global.ToDocument(tx);

                        transactions.EnsureIndex(CurrencyKey);
                        transactions.Upsert(document);
                    }

                    return Task.FromResult(true);
                }
            }
            catch (Exception e)
            {
                Log.Error(e, "Error adding transaction");
            }

            return Task.FromResult(false);
        }

        public Task<bool> AddOutputsAsync(IEnumerable<ITxOutput> outputs, Currency currency, string address)
        {
            try
            {
                lock (_syncRoot)
                {
                    using (var db = new LiteDatabase(ConnectionString))
                    {
                        var outputsCollection = db.GetCollection(OutputsCollectionName);

                        var documents = outputs
                            .Select(o =>
                            {
                                var document = BsonMapper.Global.ToDocument(o);
                                document[CurrencyKey] = currency.Name;
                                document[AddressKey] = address;
                                return document;
                            });

                        outputsCollection.EnsureIndex(CurrencyKey);
                        outputsCollection.Upsert(documents);
                    }

                    return Task.FromResult(true);
                }
            }
            catch (Exception e)
            {
                Log.Error(e, "Error adding transaction");
            }

            return Task.FromResult(false);
        }

        public Task<IBlockchainTransaction> GetTransactionByIdAsync(Currency currency, string txId)
        {
            try
            {
                lock (_syncRoot)
                {
                    using (var db = new LiteDatabase(ConnectionString))
                    {
                        var transactions = db.GetCollection(TransactionCollectionName);

                        var document = transactions.FindById(txId);

                        if (document != null)
                        {
                            var tx = (IBlockchainTransaction) BsonMapper.Global.ToObject(
                                type: currency.TransactionType,
                                doc: document);

                            return Task.FromResult(tx);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Log.Error(e, "Error getting transaction by id");
            }

            return Task.FromResult<IBlockchainTransaction>(null);
        }

        public Task<IEnumerable<IBlockchainTransaction>> GetTransactionsAsync(Currency currency)
        {
            try
            {
                lock (_syncRoot)
                {
                    using (var db = new LiteDatabase(ConnectionString))
                    {
                        var transactionsCollection = db.GetCollection(TransactionCollectionName);

                        var documents = transactionsCollection
                            .Find(d => d[CurrencyKey] == currency.Name);

                        var transactions = documents.Select(d => (IBlockchainTransaction) BsonMapper.Global.ToObject(
                            type: currency.TransactionType,
                            doc: d));

                        return Task.FromResult(transactions);
                    }
                }
            }
            catch (Exception e)
            {
                Log.Error(e, "Error getting transactions");
            }

            return Task.FromResult(Enumerable.Empty<IBlockchainTransaction>());
        }

        public async Task<IEnumerable<IBlockchainTransaction>> GetUnconfirmedTransactionsAsync(Currency currency)
        {
            var transactions = await GetTransactionsAsync(currency)
                .ConfigureAwait(false);

            return transactions.Where(t => !t.IsConfirmed());
        }

        public async Task<IEnumerable<ITxOutput>> GetUnspentOutputsAsync(Currency currency, bool skipUnconfirmed = true)
        {
            var outputs = await GetOutputsAsync(currency)
                .ConfigureAwait(false);

            if (!skipUnconfirmed)
                return outputs.Where(o => !o.IsSpent);

            return await GetUnspentConfirmedOutputsAsync(currency, outputs)
                .ConfigureAwait(false);
        }

        public async Task<IEnumerable<ITxOutput>> GetUnspentOutputsAsync(Currency currency, string address, bool skipUnconfirmed = true)
        {
            var outputs = await GetOutputsAsync(currency, address)
                .ConfigureAwait(false);

            if (!skipUnconfirmed)
                return outputs.Where(o => !o.IsSpent);

            return await GetUnspentConfirmedOutputsAsync(currency, outputs)
                .ConfigureAwait(false);
        }

        private async Task<IEnumerable<ITxOutput>> GetUnspentConfirmedOutputsAsync(
            Currency currency,
            IEnumerable<ITxOutput> outputs)
        {
            var unconfirmedTransactions = await GetUnconfirmedTransactionsAsync(currency)
                .ConfigureAwait(false);

            return outputs
                .Where(o => !o.IsSpent)
                .Where(o => unconfirmedTransactions
                    .Cast<IInOutTransaction>()
                    .FirstOrDefault(t => t.Inputs
                        .FirstOrDefault(i => i.Hash.Equals(o.TxId) && i.Index.Equals(o.Index)) != null) == null);
        }

        public Task<IEnumerable<ITxOutput>> GetOutputsAsync(Currency currency)
        {
            try
            {
                lock (_syncRoot)
                {
                    using (var db = new LiteDatabase(ConnectionString))
                    {
                        var outputsCollection = db.GetCollection(OutputsCollectionName);

                        var documents = outputsCollection
                            .Find(d => d[CurrencyKey] == currency.Name);

                        var outputs = documents.Select(d => (ITxOutput) BsonMapper.Global.ToObject(
                            type: d.OutputType(),
                            doc: d));

                        return Task.FromResult(outputs);
                    }
                }
            }
            catch (Exception e)
            {
                Log.Error(e, "Error getting outputs");
            }

            return Task.FromResult(Enumerable.Empty<ITxOutput>());
        }

        public Task<IEnumerable<ITxOutput>> GetOutputsAsync(Currency currency, string address)
        {
            try
            {
                lock (_syncRoot)
                {
                    using (var db = new LiteDatabase(ConnectionString))
                    {
                        var outputsCollection = db.GetCollection(OutputsCollectionName);

                        var documents = outputsCollection
                            .Find(d => d[CurrencyKey] == currency.Name && d[AddressKey] == address);

                        var outputs = documents.Select(d => (ITxOutput) BsonMapper.Global.ToObject(
                            type: d.OutputType(),
                            doc: d));

                        return Task.FromResult(outputs);
                    }
                }
            }
            catch (Exception e)
            {
                Log.Error(e, "Error getting outputs");
            }

            return Task.FromResult(Enumerable.Empty<ITxOutput>());
        }

        #endregion
    }
}