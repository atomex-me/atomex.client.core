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
    public class TezosWalletScanner : ICurrencyHdWalletScanner
    {
        private const int DefaultInternalLookAhead = 2;
        private const int DefaultExternalLookAhead = 2;

        //private class Scan

        private int InternalLookAhead { get; } = DefaultInternalLookAhead;
        private int ExternalLookAhead { get; } = DefaultExternalLookAhead;
        private TezosAccount Account { get; }
        private CurrencyConfig Currency => Account.Currencies.GetByName(Account.Currency);

        public TezosWalletScanner(TezosAccount account)
        {
            Account = account ?? throw new ArgumentNullException(nameof(account));
        }

        public async Task ScanAsync(
            bool skipUsed = false,
            CancellationToken cancellationToken = default)
        {
            var currency = Currency;

            var tezosAddresses = await Account.DataRepository
                .GetAddressesAsync(currency.Name)
                .ConfigureAwait(false);

            var isFirstScan = !tezosAddresses.Any();

            var scanBip32Ed25519 = isFirstScan || tezosAddresses
                .FirstOrDefault(w => w.KeyType == TezosConfig.Bip32Ed25519Key &&
                                     (w.HasActivity ||
                                     w.Balance != 0 ||
                                     w.UnconfirmedIncome != 0 ||
                                     w.UnconfirmedOutcome != 0)) != null;

            var scanParams = scanBip32Ed25519
                ? new[]
                {
                    new {KeyType = TezosConfig.Bip32Ed25519Key, Chain = Bip44.Internal, LookAhead = InternalLookAhead},
                    new {KeyType = TezosConfig.Bip32Ed25519Key, Chain = Bip44.External, LookAhead = ExternalLookAhead},
                    new {KeyType = CurrencyConfig.ClassicKey, Chain = Bip44.Internal, LookAhead = InternalLookAhead},
                    new {KeyType = CurrencyConfig.ClassicKey, Chain = Bip44.External, LookAhead = ExternalLookAhead},
                }
                : new[]
                {
                    new {KeyType = CurrencyConfig.ClassicKey, Chain = Bip44.Internal, LookAhead = InternalLookAhead},
                    new {KeyType = CurrencyConfig.ClassicKey, Chain = Bip44.External, LookAhead = ExternalLookAhead},
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
                        .DivideAddressAsync(
                            chain: param.Chain,
                            index: index,
                            keyType: param.KeyType)
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

            if (isFirstScan && scanBip32Ed25519)
            {
                // remove bip32Ed25519 addresses if there is no activity on them
                var addresses = (await Account.DataRepository
                    .GetAddressesAsync(currency.Name)
                    .ConfigureAwait(false))
                    .Where(a => a.KeyType == TezosConfig.Bip32Ed25519Key);

                foreach (var address in addresses)
                {
                    await Account.DataRepository
                        .RemoveAddressAsync(address.Currency, address.Address)
                        .ConfigureAwait(false);
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