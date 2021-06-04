using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Serilog;

using Atomex.Blockchain.Tezos;
using Atomex.Core;
using Atomex.Wallet.Abstract;
using Atomex.Wallet.Bip;

namespace Atomex.Wallet.Tezos
{
    public class Fa12WalletScanner : ICurrencyHdWalletScanner
    {
        private const int DefaultInternalLookAhead = 2;
        private const int DefaultExternalLookAhead = 2;

        private int InternalLookAhead { get; } = DefaultInternalLookAhead;
        private int ExternalLookAhead { get; } = DefaultExternalLookAhead;
        private Fa12Account Account { get; }
        private TezosAccount TezosAccount { get; }
        private CurrencyConfig Currency => Account.Currencies.GetByName(Account.Currency);

        public Fa12WalletScanner(Fa12Account account, TezosAccount tezosAccount)
        {
            Account = account ?? throw new ArgumentNullException(nameof(account));
            TezosAccount = tezosAccount ?? throw new ArgumentNullException(nameof(tezosAccount));
        }

        public async Task ScanAsync(
            bool skipUsed = false,
            CancellationToken cancellationToken = default)
        {
            var currency = Currency;

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
                        .DivideAddressAsync(param.Chain, index)
                        .ConfigureAwait(false);

                    if (walletAddress == null)
                        break;

                    Log.Debug(
                        "Scan transactions for {@name} address {@chain}:{@index}:{@address}",
                        currency.Name,
                        param.Chain,
                        index,
                        walletAddress.Address);

                    var addressTxs = await ScanAddressAsync(walletAddress.Address, cancellationToken)
                        .ConfigureAwait(false);

                    if (addressTxs == null || !addressTxs.Any()) // address without activity
                    {
                        if (TezosAccount != null) // check if address had XTZ activity to check tokens deeper
                        {
                            var tezosAddress = await TezosAccount
                                .GetAddressAsync(walletAddress.Address, cancellationToken)
                                .ConfigureAwait(false);

                            if (tezosAddress != null && tezosAddress.HasActivity)
                            {
                                freeKeysCount = 0;
                                index++;
                                continue;
                            }
                        }

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
                .UpdateBalanceAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        public async Task ScanAsync(
            string address,
            CancellationToken cancellationToken = default)
        {
            Log.Debug("Scan transactions for address {@address}", address);

            var addressTxs = await ScanAddressAsync(address, cancellationToken)
                .ConfigureAwait(false);

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
                .UpdateBalanceAsync(address, cancellationToken)
                .ConfigureAwait(false);
        }

        private async Task<IEnumerable<TezosTransaction>> ScanAddressAsync(
            string address,
            CancellationToken cancellationToken = default)
        {
            var currency = Currency;

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
                return null;
            }

            var addressTxs = txsResult.Value
                ?.Cast<TezosTransaction>()
                .ToList();

            return await Task.FromResult<IEnumerable<TezosTransaction>>(addressTxs);
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