using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Security;

using Serilog;
using Atomex.Blockchain.Abstract;
using Atomex.Blockchain.Tezos;
using Atomex.Common;
using Atomex.Core;
using Atomex.Wallet.Abstract;
using Atomex.Abstract;
using Atomex.Common.Bson;
using LiteDB;
using Newtonsoft.Json;


namespace Atomex.Wallet
{
    public class WebAccountDataRepository : IAccountDataRepository
    {
        private readonly Dictionary<string, WalletAddress> _addresses;
        private readonly Dictionary<string, IBlockchainTransaction> _transactions;
        private readonly Dictionary<string, OutputEntity> _outputs;
        private readonly Dictionary<long, Swap> _swaps;
        private readonly Dictionary<string, Order> _orders;
        private readonly Dictionary<string, WalletAddress> _tezosTokensAddresses;
        private readonly Dictionary<string, TokenTransfer> _tezosTokensTransfers;
        private readonly Dictionary<string, TokenContract> _tezosTokensContracts;

        private ICurrencies _currencies;

        private readonly object _sync;

        private BsonMapper _bsonMapper;

        public Action<AvailableDataType, string, string> SaveDataCallback;

        public enum AvailableDataType
        {
            WalletAddress,
            RemoveWalletAddress,
            Transaction,
            RemoveTransaction,
            Output,
            Swap,
            Order,
            TezosTokenAddress,
            TezosTokenTransfer,
            TezosTokenContract
        }

        public class OutputEntity
        {
            public ITxOutput Output { get; set; }
            public string Currency { get; set; }
            public string Address { get; set; }
        }

        public WebAccountDataRepository(ICurrencies currencies)
        {
            _addresses            = new Dictionary<string, WalletAddress>();
            _transactions         = new Dictionary<string, IBlockchainTransaction>();
            _outputs              = new Dictionary<string, OutputEntity>();
            _swaps                = new Dictionary<long, Swap>();
            _orders               = new Dictionary<string, Order>();
            _tezosTokensAddresses = new Dictionary<string, WalletAddress>();
            _tezosTokensTransfers = new Dictionary<string, TokenTransfer>();
            _tezosTokensContracts = new Dictionary<string, TokenContract>();
            _sync = new object();

            _bsonMapper = new BsonMapper()
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
            _currencies = currencies;
        }

        public void AddData(string data)
        {
            List<BrowserDBData> dbData = JsonConvert.DeserializeObject<List<BrowserDBData>>(data);
            foreach (var dbObj in dbData)
            {
                if (dbObj.type == AvailableDataType.WalletAddress.ToString())
                {
                    _addresses[dbObj.id] =
                        _bsonMapper.ToObject<WalletAddress>(
                            BsonSerializer.Deserialize(Convert.FromBase64String(dbObj.data)));
                }
                else if (dbObj.type == AvailableDataType.Transaction.ToString())
                {
                    string[] parsedId = dbObj.id.Split(Convert.ToChar("/"));
                    string id = parsedId[0];
                    string currency = parsedId[1];

                    BsonDocument bd = BsonSerializer.Deserialize(Convert.FromBase64String(dbObj.data));
                    _transactions[$"{id}:{currency}"] = (IBlockchainTransaction) _bsonMapper.ToObject(doc: bd,
                        type: _currencies.GetByName(currency).TransactionType);
                }
                else if (dbObj.type == AvailableDataType.Swap.ToString())
                {
                    _swaps[long.Parse(dbObj.id)] =
                        _bsonMapper.ToObject<Swap>(
                            BsonSerializer.Deserialize(Convert.FromBase64String(dbObj.data)));
                }
                else if (dbObj.type == AvailableDataType.Output.ToString())
                {
                    string[] parsedId = dbObj.id.Split(Convert.ToChar("/"));
                    string id = parsedId[0];
                    string currency = parsedId[1];
                    string address = parsedId[2];

                    BsonDocument bd = BsonSerializer.Deserialize(Convert.FromBase64String(dbObj.data));
                    BitcoinBasedConfig BtcBasedCurrency = _currencies.Get<BitcoinBasedConfig>(currency);
                    ITxOutput output =
                        (ITxOutput) _bsonMapper.ToObject(doc: bd, type: BtcBasedCurrency.OutputType());

                    _outputs[id] = new OutputEntity {Output = output, Currency = currency, Address = address};
                }
                else if (dbObj.type == AvailableDataType.Order.ToString())
                {
                    _orders[dbObj.id] =
                        _bsonMapper.ToObject<Order>(
                            BsonSerializer.Deserialize(Convert.FromBase64String(dbObj.data)));
                }

                else if (dbObj.type == AvailableDataType.TezosTokenAddress.ToString())
                {
                    _tezosTokensAddresses[dbObj.id] = _bsonMapper.ToObject<WalletAddress>(
                        BsonSerializer.Deserialize(Convert.FromBase64String(dbObj.data)));
                }

                else if (dbObj.type == AvailableDataType.TezosTokenContract.ToString())
                {
                    _tezosTokensContracts[dbObj.id] =
                        _bsonMapper.ToObject<TokenContract>(
                            BsonSerializer.Deserialize(Convert.FromBase64String(dbObj.data)));
                }

                else if (dbObj.type == AvailableDataType.TezosTokenTransfer.ToString())
                {
                    _tezosTokensTransfers[dbObj.id] =
                        _bsonMapper.ToObject<TokenTransfer>(
                            BsonSerializer.Deserialize(Convert.FromBase64String(dbObj.data)));
                }
            }
        }

        #region Addresses

        public virtual Task<bool> UpsertAddressAsync(WalletAddress walletAddress)
        {
            lock (_sync)
            {
                var walletId = $"{walletAddress.Currency}:{walletAddress.Address}";

                _addresses[walletId] = walletAddress.Copy();

                var data = Convert.ToBase64String(BsonSerializer.Serialize(_bsonMapper.ToDocument(walletAddress)));

                SaveDataCallback?.Invoke(AvailableDataType.WalletAddress, walletId, data);

                return Task.FromResult(true);
            }
        }

        public virtual Task<int> UpsertAddressesAsync(IEnumerable<WalletAddress> walletAddresses)
        {
            lock (_sync)
            {
                foreach (var walletAddress in walletAddresses)
                {
                    var walletId = $"{walletAddress.Currency}:{walletAddress.Address}";

                    _addresses[walletId] = walletAddress.Copy();

                    var data = Convert.ToBase64String(BsonSerializer.Serialize(_bsonMapper.ToDocument(walletAddress)));

                    SaveDataCallback?.Invoke(AvailableDataType.WalletAddress, walletId, data);
                }

                return Task.FromResult(walletAddresses.Count());
            }
        }

        public virtual Task<bool> TryInsertAddressAsync(WalletAddress walletAddress)
        {
            lock (_sync)
            {
                var walletId = $"{walletAddress.Currency}:{walletAddress.Address}";
                WalletAddress existsAddress;
                
                if (!_addresses.TryGetValue(walletId, out existsAddress))
                {
                    _addresses[walletId] = walletAddress.Copy();
                    
                    var data = Convert.ToBase64String(BsonSerializer.Serialize(_bsonMapper.ToDocument(walletAddress)));
                    SaveDataCallback?.Invoke(AvailableDataType.WalletAddress, walletId, data);
                    return Task.FromResult(true);
                }
                
                if (existsAddress.KeyType != walletAddress.KeyType)
                {
                    existsAddress.KeyType          = walletAddress.KeyType;
                    existsAddress.KeyIndex.Chain   = walletAddress.KeyIndex.Chain;
                    existsAddress.KeyIndex.Index   = walletAddress.KeyIndex.Index;
                    existsAddress.KeyIndex.Account = walletAddress.KeyIndex.Account;
                    _addresses[walletId] = existsAddress.Copy();
                    
                    var data = Convert.ToBase64String(BsonSerializer.Serialize(_bsonMapper.ToDocument(existsAddress)));
                    SaveDataCallback?.Invoke(AvailableDataType.WalletAddress, walletId, data);
                    return Task.FromResult(true);
                }

                return Task.FromResult(false);
            }
        }

        public virtual Task<WalletAddress> GetWalletAddressAsync(string currency, string address)
        {
            lock (_sync)
            {
                var walletId = $"{currency}:{address}";

                if (_addresses.TryGetValue(walletId, out var walletAddress))
                    return Task.FromResult(walletAddress.Copy());

                return Task.FromResult<WalletAddress>(null);
            }
        }

        public virtual Task<WalletAddress> GetLastActiveWalletAddressAsync(
            string currency,
            uint chain,
            int keyType)
        {
            lock (_sync)
            {
                var address = _addresses.Values
                    .Where(w => w.Currency == currency &&
                                w.KeyIndex.Chain == chain &&
                                w.KeyType == keyType &&
                                w.HasActivity)
                    .OrderByDescending(w => w.KeyIndex.Index)
                    .FirstOrDefault();

                return address != null
                    ? Task.FromResult(address.Copy())
                    : Task.FromResult<WalletAddress>(null);
            }
        }

        public Task<WalletAddress> GetLastActiveWalletAddressByAccountAsync(
            string currency,
            int keyType)
        {
            lock (_sync)
            {
                var address = _addresses.Values
                    .Where(w => w.Currency == currency &&
                                w.KeyType == keyType &&
                                w.HasActivity)
                    .OrderByDescending(w => w.KeyIndex.Account)
                    .FirstOrDefault();

                return address != null
                    ? Task.FromResult(address)
                    : Task.FromResult<WalletAddress>(null);
            }
        }

        public virtual Task<IEnumerable<WalletAddress>> GetUnspentAddressesAsync(
            string currency,
            bool includeUnconfirmed = true)
        {
            lock (_sync)
            {
                var addresses = includeUnconfirmed
                    ? _addresses.Values
                        .Where(w => w.Currency == currency &&
                                    (w.Balance != 0 || w.UnconfirmedIncome != 0 || w.UnconfirmedOutcome != 0))
                    : _addresses.Values
                        .Where(w => w.Currency == currency && w.Balance != 0);
                return Task.FromResult(addresses.Select(a => a.Copy()));
            }
        }

        public virtual Task<IEnumerable<WalletAddress>> GetAddressesAsync(
            string currency)
        {
            lock (_sync)
            {
                var addresses = _addresses.Values
                    .Where(w => w.Currency == currency)
                    .Select(w => w.Copy());

                return Task.FromResult(addresses);
            }
        }

        public Task<bool> RemoveAddressAsync(
            string currency,
            string address)
        {
            try
            {
                lock (_sync)
                {
                    var walletId = $"{currency}:{address}";

                    SaveDataCallback?.Invoke(AvailableDataType.RemoveWalletAddress, walletId, null);

                    return Task.FromResult(_addresses.Remove(walletId));
                }
            }
            catch (Exception e)
            {
                Log.Error(e, "Error getting wallet addresses");
            }

            return Task.FromResult(false);
        }

        #endregion Addresses

        #region TezosTokens

        public Task<WalletAddress> GetTezosTokenAddressAsync(
            string currency,
            string tokenContract,
            decimal tokenId,
            string address)
        {
            lock (_sync)
            {
                var walletId = $"{currency}:{tokenContract}:{tokenId}:{address}";

                if (_tezosTokensAddresses.TryGetValue(walletId, out var walletAddress))
                    return Task.FromResult(walletAddress.Copy());

                return Task.FromResult<WalletAddress>(null);
            }
        }

        public Task<IEnumerable<WalletAddress>> GetTezosTokenAddressesAsync()
        {
            lock (_sync)
            {
                return Task.FromResult<IEnumerable<WalletAddress>>(
                    _tezosTokensAddresses
                        .Values
                        .Select(a => a.Copy()));
            }
        }

        public Task<IEnumerable<WalletAddress>> GetTezosTokenAddressesAsync(
            string address)
        {
            lock (_sync)
            {
                var addresses = _tezosTokensAddresses.Values
                    .Where(w => w.Address == address);

                return Task.FromResult(addresses);
            }
        }

        public Task<IEnumerable<WalletAddress>> GetTezosTokenAddressesAsync(
            string address,
            string tokenContract)
        {
            lock (_sync)
            {
                var addresses = _tezosTokensAddresses.Values
                    .Where(w => w.Address == address && w.TokenBalance.Token.Contract == tokenContract)
                    .ToList();

                return Task.FromResult<IEnumerable<WalletAddress>>(addresses);
            }
        }

        public Task<IEnumerable<WalletAddress>> GetTezosTokenAddressesByContractAsync(
            string tokenContract)
        {
            lock (_sync)
            {
                var addresses = _tezosTokensAddresses.Values
                    .Where(w => w.TokenBalance.Token.Contract == tokenContract)
                    .ToList();

                return Task.FromResult<IEnumerable<WalletAddress>>(addresses);
            }
        }

        public Task<int> UpsertTezosTokenAddressesAsync(
            IEnumerable<WalletAddress> walletAddresses)
        {
            lock (_sync)
            {
                foreach (var wa in walletAddresses)
                {
                    var walletId = $"{wa.Currency}:{wa.TokenBalance.Token.Contract}:{wa.TokenBalance.Token.TokenId}:{wa.Address}";

                    _tezosTokensAddresses[walletId] = wa.Copy();

                    var data = Convert.ToBase64String(BsonSerializer.Serialize(_bsonMapper.ToDocument(wa)));
                    SaveDataCallback?.Invoke(AvailableDataType.TezosTokenAddress, walletId, data);
                }

                return Task.FromResult(walletAddresses.Count());
            }
        }

        public Task<IEnumerable<WalletAddress>> GetUnspentTezosTokenAddressesAsync(
            string currency,
            string tokenContract,
            decimal tokenId)
        {
            lock (_sync)
            {
                var addresses = _tezosTokensAddresses.Values
                    .Where(w =>
                        //w.Currency == currency && 
                        w.TokenBalance.Token.Contract == tokenContract &&
                        w.TokenBalance.Token.TokenId == tokenId &&
                        (w.Balance != 0 ||
                         w.UnconfirmedIncome != 0 ||
                         w.UnconfirmedOutcome != 0));

                return Task.FromResult(addresses);
            }
        }

        public Task<bool> TryInsertTezosTokenAddressAsync(WalletAddress address)
        {
            lock (_sync)
            {
                var walletId =
                    $"{address.Currency}:{address.TokenBalance.Token.Contract}:{address.TokenBalance.Token.TokenId}:{address.Address}";

                if (_tezosTokensAddresses.ContainsKey(walletId))
                    return Task.FromResult(false);

                _tezosTokensAddresses[walletId] = address.Copy();

                var data = Convert.ToBase64String(BsonSerializer.Serialize(_bsonMapper.ToDocument(address)));
                SaveDataCallback?.Invoke(AvailableDataType.TezosTokenAddress, walletId, data);

                return Task.FromResult(true);
            }
        }

        public Task<int> UpsertTezosTokenTransfersAsync(
            IEnumerable<TokenTransfer> tokenTransfers)
        {
            lock (_sync)
            {
                foreach (var tokenTransfer in tokenTransfers)
                {
                    _tezosTokensTransfers[tokenTransfer.Id] = tokenTransfer; // todo: copy ?

                    var data = Convert.ToBase64String(BsonSerializer.Serialize(_bsonMapper.ToDocument(tokenTransfer)));
                    SaveDataCallback?.Invoke(AvailableDataType.TezosTokenTransfer, tokenTransfer.Id, data);
                }

                return Task.FromResult(tokenTransfers.Count());
            }
        }

        public Task<IEnumerable<TokenTransfer>> GetTezosTokenTransfersAsync(
            string contractAddress,
            int offset = 0,
            int limit = 20)
        {
            lock (_sync)
            {
                var txs = _tezosTokensTransfers.Values
                    .Where(t => t.Token.Contract == contractAddress)
                    .ToList()
                    .SortList((t1, t2) => t1.TimeStamp.CompareTo(t2.TimeStamp))
                    .Skip(offset)
                    .Take(limit);

                return Task.FromResult(txs);
            }
        }

        public Task<int> UpsertTezosTokenContractsAsync(
            IEnumerable<TokenContract> tokenContracts)
        {
            lock (_sync)
            {
                foreach (var tc in tokenContracts)
                {
                    _tezosTokensContracts[tc.Id] = tc; // todo: copy?

                    var data = Convert.ToBase64String(BsonSerializer.Serialize(_bsonMapper.ToDocument(tc)));
                    SaveDataCallback?.Invoke(AvailableDataType.TezosTokenContract, tc.Id, data);
                }

                return Task.FromResult(tokenContracts.Count());
            }
        }

        public Task<IEnumerable<TokenContract>> GetTezosTokenContractsAsync()
        {
            lock (_sync)
            {
                return Task.FromResult<IEnumerable<TokenContract>>(_tezosTokensContracts.Values);
            }
        }

        #endregion TezosTokens

        #region Transactions

        public virtual Task<bool> UpsertTransactionAsync(IBlockchainTransaction tx)
        {
            lock (_sync)
            {
                var txAge = DateTime.Now - tx.CreationTime;
                if (_transactions.ContainsKey($"{tx.Id}:{tx.Currency}") &&
                    tx.State == BlockchainTransactionState.Confirmed && txAge.Value.TotalDays >= 1)
                {
                    // todo: remove this;
                    return Task.FromResult(true);
                }

                _transactions[$"{tx.Id}:{tx.Currency}"] = tx; // todo: copy?

                var data = Convert.ToBase64String(
                    BsonSerializer.Serialize(_bsonMapper.ToDocument<IBlockchainTransaction>(tx)));
                SaveDataCallback?.Invoke(AvailableDataType.Transaction, $"{tx.Id}/{tx.Currency}", data);

                return Task.FromResult(true);
            }
        }

        public virtual Task<IBlockchainTransaction> GetTransactionByIdAsync(
            string currency,
            string txId,
            Type transactionType)
        {
            lock (_sync)
            {
                if (_transactions.TryGetValue($"{txId}:{currency}", out var tx))
                {
                    return Task.FromResult(tx);
                }

                return Task.FromResult<IBlockchainTransaction>(null);
            }
        }

        public virtual Task<IEnumerable<IBlockchainTransaction>> GetTransactionsAsync(
            string currency,
            Type transactionType)
        {
            lock (_sync)
            {
                var txs = _transactions.Values
                    .Where(t => t.Currency == currency);

                return Task.FromResult(txs);
            }
        }

        public virtual Task<IEnumerable<IBlockchainTransaction>> GetUnconfirmedTransactionsAsync(
            string currency,
            Type transactionType)
        {
            lock (_sync)
            {
                var txs = _transactions.Values
                    .Where(t => t.Currency == currency && !t.IsConfirmed);

                return Task.FromResult(txs);
            }
        }

        public virtual Task<bool> RemoveTransactionByIdAsync(string id)
        {
            lock (_sync)
            {
                string[] parsedId = id.Split(Convert.ToChar(":"));
                string txId = parsedId[0];
                string currencyName = parsedId[1];

                SaveDataCallback?.Invoke(AvailableDataType.RemoveTransaction, $"{txId}/{currencyName}", null);

                return Task.FromResult(_transactions.Remove(id));
            }
        }

        #endregion Transactions

        #region Outputs

        public virtual Task<bool> UpsertOutputsAsync(
            IEnumerable<ITxOutput> outputs,
            string currency,
            string address)
        {
            lock (_sync)
            {
                foreach (var output in outputs)
                {
                    var id = $"{output.TxId}:{output.Index}";
                    var entity = new OutputEntity
                    {
                        Output = output, // todo: copy?
                        Currency = currency,
                        Address = address
                    };

                    _outputs[id] = entity;

                    var data = Convert.ToBase64String(BsonSerializer.Serialize(_bsonMapper.ToDocument(output)));
                    SaveDataCallback?.Invoke(AvailableDataType.Output, $"{id}/{currency}/{address}", data);
                }

                return Task.FromResult(true);
            }
        }

        public virtual async Task<IEnumerable<ITxOutput>> GetAvailableOutputsAsync(
            string currency,
            Type outputType,
            Type transactionType)
        {
            var outputs = (await GetOutputsAsync(currency, outputType)
                    .ConfigureAwait(false))
                .Where(o => !o.IsSpent)
                .ToList();

            return await GetOnlyConfirmedOutputsAsync(currency, outputs, transactionType)
                .ConfigureAwait(false);
        }

        public virtual async Task<IEnumerable<ITxOutput>> GetAvailableOutputsAsync(
            string currency,
            string address,
            Type outputType,
            Type transactionType)
        {
            var outputs = (await GetOutputsAsync(currency, address, outputType)
                    .ConfigureAwait(false))
                .Where(o => !o.IsSpent)
                .ToList();

            return await GetOnlyConfirmedOutputsAsync(currency, outputs, transactionType)
                .ConfigureAwait(false);
        }

        private async Task<IEnumerable<ITxOutput>> GetOnlyConfirmedOutputsAsync(
            string currency,
            IEnumerable<ITxOutput> outputs,
            Type transactionType)
        {
            var confirmedOutputs = new List<ITxOutput>();

            foreach (var o in outputs)
            {
                var tx = await GetTransactionByIdAsync(currency, o.TxId, transactionType)
                    .ConfigureAwait(false);

                if (tx?.IsConfirmed ?? false)
                    confirmedOutputs.Add(o);
            }

            return confirmedOutputs;
        }

        public virtual Task<IEnumerable<ITxOutput>> GetOutputsAsync(
            string currency,
            Type outputType)
        {
            lock (_sync)
            {
                IEnumerable<ITxOutput> outputs = _outputs.Values
                    .Where(o => o.Currency == currency)
                    .Select(o => o.Output)
                    .ToList();

                return Task.FromResult(outputs);
            }
        }

        public virtual Task<IEnumerable<ITxOutput>> GetOutputsAsync(
            string currency,
            string address,
            Type outputType)
        {
            lock (_sync)
            {
                IEnumerable<ITxOutput> outputs = _outputs.Values
                    .Where(o => o.Currency == currency && o.Address == address)
                    .Select(o => o.Output)
                    .ToList();

                return Task.FromResult(outputs);
            }
        }

        public virtual Task<ITxOutput> GetOutputAsync(
            string currency,
            string txId,
            uint index,
            Type outputType)
        {
            lock (_sync)
            {
                var id = $"{txId}:{index}";

                if (_outputs.TryGetValue(id, out var output))
                    return Task.FromResult(output.Output);

                return Task.FromResult<ITxOutput>(null);
            }
        }

        #endregion Outputs

        #region Orders

        public virtual Task<bool> UpsertOrderAsync(Order order)
        {
            lock (_sync)
            {
                if (!VerifyOrder(order))
                    return Task.FromResult(false);

                _orders[order.ClientOrderId] = order; // todo: copy?

                var data = Convert.ToBase64String(BsonSerializer.Serialize(_bsonMapper.ToDocument(order)));
                SaveDataCallback?.Invoke(AvailableDataType.Order, order.ClientOrderId, data);

                return Task.FromResult(true);
            }
        }

        public virtual Order GetOrderById(string clientOrderId)
        {
            lock (_sync)
            {
                if (_orders.TryGetValue(clientOrderId, out var order))
                    return order;

                return null;
            }
        }

        private bool VerifyOrder(Order order)
        {
            if (order.Status == OrderStatus.Pending)
            {
                var pendingOrder = GetPendingOrder(order.ClientOrderId);

                if (pendingOrder != null)
                {
                    Log.Error("Order already pending");

                    return false;
                }
            }
            else if (order.Status == OrderStatus.Placed || order.Status == OrderStatus.Rejected)
            {
                var pendingOrder = GetPendingOrder(order.ClientOrderId);

                if (pendingOrder == null)
                {
                    order.IsApproved = false;

                    // probably a different device order
                    Log.Information("Probably order from another device: {@order}",
                        order.ToString());
                }
                else
                {
                    if (pendingOrder.Status == OrderStatus.Rejected)
                    {
                        Log.Error("Order already rejected");

                        return false;
                    }

                    if (!order.IsContinuationOf(pendingOrder))
                    {
                        Log.Error(
                            "Order is not continuation of saved pending order! Order: {@order}, pending order: {@pendingOrder}",
                            order.ToString(),
                            pendingOrder.ToString());

                        return false;
                    }

                    // forward local params
                    order.IsApproved = pendingOrder.IsApproved;
                    order.MakerNetworkFee = pendingOrder.MakerNetworkFee;
                }
            }
            else
            {
                var actualOrder = GetOrderById(order.ClientOrderId);

                if (actualOrder == null)
                {
                    Log.Error("Order is not continuation of saved order! Order: {@order}",
                        order.ToString());

                    return false;
                }

                if (!order.IsContinuationOf(actualOrder))
                {
                    Log.Error("Order is not continuation of saved order! Order: {@order}, saved order: {@actualOrder}",
                        order.ToString(),
                        actualOrder.ToString());

                    return false;
                }

                // forward local params
                order.IsApproved = actualOrder.IsApproved;
                order.MakerNetworkFee = actualOrder.MakerNetworkFee;
            }

            return true;
        }

        public virtual Order GetOrderById(long id)
        {
            lock (_sync)
            {
                return _orders.Values.SingleOrDefault(o => o.Id == id);
            }
        }

        private Order GetPendingOrder(string clientOrderId)
        {
            lock (_sync)
            {
                if (_orders.TryGetValue(clientOrderId, out var order))
                    return order.Id == 0 ? order : null;

                return null;
            }
        }

        #endregion Orders

        #region Swaps

        public virtual Task<bool> AddSwapAsync(Swap swap)
        {
            lock (_sync)
            {
                _swaps[swap.Id] = swap; // todo: copy?

                var data = Convert.ToBase64String(BsonSerializer.Serialize(_bsonMapper.ToDocument(swap)));
                SaveDataCallback?.Invoke(AvailableDataType.Swap, swap.Id.ToString(), data);

                return Task.FromResult(true);
            }
        }

        public virtual Task<bool> UpdateSwapAsync(Swap swap)
        {
            return AddSwapAsync(swap);
        }

        public virtual Task<Swap> GetSwapByIdAsync(long id)
        {
            lock (_sync)
            {
                if (_swaps.TryGetValue(id, out var swap))
                    return Task.FromResult(swap);

                return Task.FromResult<Swap>(null);
            }
        }

        public virtual Task<IEnumerable<Swap>> GetSwapsAsync()
        {
            lock (_sync)
            {
                return Task.FromResult<IEnumerable<Swap>>(_swaps.Values.ToList()); // todo: copy!
            }
        }

        #endregion Swaps

        public void ChangePassword(SecureString newPassword)
        {
            throw new NotImplementedException();
        }
    }

    public class BrowserDBData
    {
        public string type;
        public string id;
        public string data;
    }
}