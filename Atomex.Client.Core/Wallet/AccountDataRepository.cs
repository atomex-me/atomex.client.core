using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Atomex.Blockchain.Abstract;
using Atomex.Common;
using Atomex.Core;
using Atomex.Wallet.Abstract;
using Serilog;

namespace Atomex.Wallet
{
    public class AccountDataRepository : IAccountDataRepository
    {
        private readonly Dictionary<string, WalletAddress> _addresses;
        private readonly Dictionary<string, IBlockchainTransaction> _transactions;
        private readonly Dictionary<string, OutputEntity> _outputs;
        private readonly Dictionary<long, Swap> _swaps;
        private readonly Dictionary<string, Order> _orders;

        private readonly object _sync;

        private class OutputEntity
        {
            public ITxOutput Output { get; set; }
            public string Currency { get; set; }
            public string Address { get; set; }
        }

        public AccountDataRepository()
        {
            _addresses = new Dictionary<string, WalletAddress>();
            _transactions = new Dictionary<string, IBlockchainTransaction>();
            _outputs = new Dictionary<string, OutputEntity>();
            _swaps = new Dictionary<long, Swap>();
            _orders = new Dictionary<string, Order>();
            _sync = new object();
        }

        #region Addresses

        public virtual Task<bool> UpsertAddressAsync(WalletAddress walletAddress)
        {
            lock (_sync)
            {
                var walletId = $"{walletAddress.Currency}:{walletAddress.Address}";

                _addresses[walletId] = walletAddress; // todo: copy?

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

                    _addresses[walletId] = walletAddress; // todo: copy?
                }

                return Task.FromResult(walletAddresses.Count());
            }
        }

        public virtual Task<bool> TryInsertAddressAsync(WalletAddress walletAddress)
        {
            lock (_sync)
            {
                var walletId = $"{walletAddress.Currency}:{walletAddress.Address}";

                if (_addresses.ContainsKey(walletId))
                    return Task.FromResult(false);

                _addresses[walletId] = walletAddress; // todo: copy?

                return Task.FromResult(true);
            }
        }

        public virtual Task<WalletAddress> GetWalletAddressAsync(string currency, string address)
        {
            lock (_sync)
            {
                var walletId = $"{currency}:{address}";

                if (_addresses.TryGetValue(walletId, out var walletAddress))
                    return Task.FromResult(walletAddress);

                return Task.FromResult<WalletAddress>(null);
            }
        }

        public virtual Task<WalletAddress> GetLastActiveWalletAddressAsync(string currency, int chain)
        {
            lock (_sync)
            {
                var address = _addresses.Values
                    .Where(w => w.Currency == currency && w.KeyIndex.Chain == chain && w.HasActivity)
                    .OrderByDescending(w => w.KeyIndex.Index)
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
                        .Where(w => w.Currency == currency && w.Balance != 0 && w.UnconfirmedIncome != 0 && w.UnconfirmedOutcome != 0)
                    : _addresses.Values
                        .Where(w => w.Currency == currency && w.Balance != 0);

                return Task.FromResult(addresses);
            }
        }

        #endregion Addresses

        #region Transactions

        public virtual Task<bool> UpsertTransactionAsync(IBlockchainTransaction tx)
        {
            lock (_sync)
            {
                _transactions[tx.Id] = tx; // todo: copy?

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
                if (_transactions.TryGetValue(txId, out var tx))
                    return Task.FromResult(tx);

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
                    .Where(t => t.Currency.Name == currency);

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
                    .Where(t => t.Currency.Name == currency && !t.IsConfirmed);

                return Task.FromResult(txs);
            }
        }

        public virtual Task<bool> RemoveTransactionByIdAsync(string id)
        {
            lock (_sync)
            {
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

                    _outputs[id] = new OutputEntity
                    {
                        Output = output, // todo: copy?
                        Currency = currency,
                        Address = address
                    };
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
                var outputs = _outputs.Values
                    .Where(o => o.Currency == currency)
                    .Select(o => o.Output);

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
                var outputs = _outputs.Values
                    .Where(o => o.Currency == currency && o.Address == address)
                    .Select(o => o.Output);

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

                return null;
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
                        Log.Error("Order is not continuation of saved pending order! Order: {@order}, pending order: {@pendingOrder}",
                            order.ToString(),
                            pendingOrder.ToString());

                        return false;
                    }
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

                order.IsApproved = actualOrder.IsApproved; // save approve
            }

            return true;
        }

        #endregion Orders

        #region Swaps

        public virtual Task<bool> AddSwapAsync(Swap swap)
        {
            lock (_sync)
            {
                _swaps[swap.Id] = swap; // todo: copy?

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
    }
}