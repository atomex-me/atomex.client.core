using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Atomex.Blockchain.Tezos;
using Atomex.Wallet.Abstract;
using Atomex.Wallet.Bip;
using Serilog;

namespace Atomex.Wallet.Tezos
{
    public class TezosWalletScanner : ICurrencyHdWalletScanner
    {
        private const int DefaultInternalLookAhead = 3;
        private const int DefaultExternalLookAhead = 3;

        private int InternalLookAhead { get; } = DefaultInternalLookAhead;
        private int ExternalLookAhead { get; } = DefaultExternalLookAhead;
        private IAccount Account { get; }

        public TezosWalletScanner(IAccount account)
        {
            Account = account ?? throw new ArgumentNullException(nameof(account));
        }

        public async Task ScanAsync(
            bool skipUsed = false,
            CancellationToken cancellationToken = default)
        {
            var currency = Account.Currencies.Get<Atomex.Tezos>();

            var scanParams = new[]
            {
                new {Chain = HdKeyStorage.NonHdKeysChain, LookAhead = 0},
                new {Chain = Bip44.Internal, LookAhead = InternalLookAhead},
                new {Chain = Bip44.External, LookAhead = ExternalLookAhead},
            };

            var txs = new List<TezosTransaction>();
            var txsById = new Dictionary<string, TezosTransaction>();
            var internalTxs = new List<TezosTransaction>();

            foreach (var param in scanParams)
            {
                var freeKeysCount = 0;
                var index = 0u;

                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var walletAddress = await Account
                        .DivideAddressAsync(currency, param.Chain, index)
                        .ConfigureAwait(false);

                    if (walletAddress == null)
                        break;

                    Log.Debug(
                        "Scan transactions for {@name} address {@chain}:{@index}:{@address}",
                        currency.Name,
                        param.Chain,
                        index,
                        walletAddress.Address);

                    var txsResult = await ((ITezosBlockchainApi) currency.BlockchainApi)
                        .TryGetTransactionsAsync(walletAddress.Address, cancellationToken: cancellationToken)
                        .ConfigureAwait(false);
 
                    if (txsResult.HasError)
                    {
                        Log.Error(
                            "Error while scan address transactions for {@address} with code {@code} and description {@description}", 
                            walletAddress.Address,
                            txsResult.Error.Code,
                            txsResult.Error.Description);
                        break;
                    }

                    var addressTxs = txsResult.Value
                        ?.Cast<TezosTransaction>()
                        .ToList();

                    if (addressTxs == null || !addressTxs.Any()) // address without activity
                    {
                        freeKeysCount++;

                        if (freeKeysCount >= param.LookAhead)
                        {
                            Log.Debug("{@lookAhead} free keys found. Chain scan completed", param.LookAhead);
                            break;
                        }
                    }
                    else // address has activity
                    {
                        freeKeysCount = 0;

                        foreach (var tx in addressTxs)
                        {
                            if (tx.IsInternal)
                                internalTxs.Add(tx);
                            else if (!txsById.ContainsKey(tx.Id))
                                txsById.Add(tx.Id, tx);
                        }
                    }

                    index++;
                }
            }

            // distribute internal txs
            foreach (var internalTx in internalTxs)
            {
                if (txsById.TryGetValue(internalTx.Id, out var tx))
                {
                    if (tx.InternalTxs == null)
                        tx.InternalTxs = new List<TezosTransaction>();

                    tx.InternalTxs.Add(internalTx);
                }
                else txs.Add(internalTx);
            }

            txs.AddRange(txsById.Values);

            if (txs.Any())
                await UpsertTransactionsAsync(txs)
                    .ConfigureAwait(false);

            await Account
                .UpdateBalanceAsync(
                    currency: currency,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }

        public async Task ScanAsync(
            string address,
            CancellationToken cancellationToken = default)
        {
            var currency = Account.Currencies.Get<Atomex.Tezos>();

            Log.Debug("Scan transactions for address {@address}", address);

            var txsResult = await ((ITezosBlockchainApi)currency.BlockchainApi)
                .TryGetTransactionsAsync(address, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (txsResult.HasError)
            {
                Log.Error(
                    "Error while scan address transactions for {@address} with code {@code} and description {@description}",
                    address,
                    txsResult.Error.Code,
                    txsResult.Error.Description);

                return;
            }

            var addressTxs = txsResult.Value
                ?.Cast<TezosTransaction>()
                .ToList();

            if (addressTxs == null || !addressTxs.Any()) // address without activity
                return;

            var txs = new List<TezosTransaction>();
            var txsById = new Dictionary<string, TezosTransaction>();
            var internalTxs = new List<TezosTransaction>();

            foreach (var tx in addressTxs)
            {
                if (tx.IsInternal)
                    internalTxs.Add(tx);
                else if (!txsById.ContainsKey(tx.Id))
                    txsById.Add(tx.Id, tx);
            }

            // distribute internal txs
            foreach (var internalTx in internalTxs)
            {
                if (txsById.TryGetValue(internalTx.Id, out var tx))
                {
                    if (tx.InternalTxs == null)
                        tx.InternalTxs = new List<TezosTransaction>();

                    tx.InternalTxs.Add(internalTx);
                }
                else txs.Add(internalTx);             
            }

            txs.AddRange(txsById.Values);

            if (txs.Any())
                await UpsertTransactionsAsync(txs)
                    .ConfigureAwait(false);

            await Account
                .UpdateBalanceAsync(
                    currency: currency,
                    address: address,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }

        private async Task UpsertTransactionsAsync(IEnumerable<TezosTransaction> transactions)
        {
            foreach (var tx in transactions)
            {
                await Account
                    .UpsertTransactionAsync(
                        tx: tx,
                        updateBalance: false,
                        notifyIfUnconfirmed: false,
                        notifyIfBalanceUpdated: false)
                    .ConfigureAwait(false);
            }
        }
    }
}