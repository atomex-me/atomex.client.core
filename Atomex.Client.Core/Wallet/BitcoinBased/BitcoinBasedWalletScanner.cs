using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Atomex.Blockchain.Abstract;
using Atomex.Common;
using Atomex.Core.Entities;
using Atomex.Wallet.Abstract;
using Atomex.Wallet.Bip;
using Serilog;

namespace Atomex.Wallet.BitcoinBased
{
    public class BitcoinBasedWalletScanner : ICurrencyHdWalletScanner
    {
        private const int DefaultInternalLookAhead = 3;
        private const int DefaultExternalLookAhead = 3;

        private IAccount Account { get; }
        private Currency Currency { get; }
        private int InternalLookAhead { get; } = DefaultInternalLookAhead;
        private int ExternalLookAhead { get; } = DefaultExternalLookAhead;

        public BitcoinBasedWalletScanner(Currency currency, IAccount account)
        {
            Currency = currency ?? throw new ArgumentNullException(nameof(currency));
            Account = account ?? throw new ArgumentNullException(nameof(account));
        }

        public async Task ScanAsync(
            bool skipUsed = false,
            CancellationToken cancellationToken = default)
        {
            await ScanOutputsAsync(
                    skipUsed: skipUsed,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            await ScanTransactionsAsync(cancellationToken)
                .ConfigureAwait(false);

            await Account
                .UpdateBalanceAsync(
                    currency: Currency,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }

        public async Task ScanAsync(
            string address,
            CancellationToken cancellationToken = default)
        {
            Log.Debug("Scan {@currency} outputs for {@address}",
                Currency.Name,
                address);

            var outputsResult = await ((IInOutBlockchainApi) Currency.BlockchainApi)
                .GetOutputsAsync(address, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (outputsResult.HasError)
            {
                Log.Error(
                    "Error while scan outputs for {@address} with code {@code} and description {@description}",
                    address,
                    outputsResult.Error.Code,
                    outputsResult.Error.Description);
                return;
            }

            var outputs = outputsResult.Value?.RemoveDuplicates().ToList();

            if (outputs == null || !outputs.Any())
                return;

            await Account
                .UpsertOutputsAsync(
                    outputs: outputs,
                    currency: Currency,
                    address: address,
                    notifyIfBalanceUpdated: false)
                .ConfigureAwait(false);

            await ScanTransactionsAsync(outputs, cancellationToken)
                .ConfigureAwait(false);

            await Account
                .UpdateBalanceAsync(
                    currency: Currency,
                    address: address,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }

        private async Task ScanOutputsAsync(
           bool skipUsed,
           CancellationToken cancellationToken = default)
        {
            Log.Debug("Scan outputs for {@name}", Currency.Name);

            var scanParams = new[]
            {
                new {Chain = HdKeyStorage.NonHdKeysChain, LookAhead = 0},
                new {Chain = Bip44.Internal, LookAhead = InternalLookAhead},
                new {Chain = Bip44.External, LookAhead = ExternalLookAhead},
            };

            foreach (var param in scanParams)
            {
                var freeKeysCount = 0;
                var index = 0u;

                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var walletAddress = await Account
                        .DivideAddressAsync(Currency, param.Chain, index)
                        .ConfigureAwait(false);

                    if (walletAddress == null)
                        break;

                    if (skipUsed) // check, if the address marked as "used" and skip in this case
                    {
                        var resolvedAddress = await Account
                            .ResolveAddressAsync(Currency, walletAddress.Address, cancellationToken)
                            .ConfigureAwait(false);

                        if (resolvedAddress != null &&
                            resolvedAddress.HasActivity &&
                            resolvedAddress.Balance == 0)
                        {
                            freeKeysCount = 0;
                            index++;

                            continue;
                        }
                    }

                    Log.Debug(
                        "Scan outputs for {@name} address {@chain}:{@index}:{@address}",
                        Currency.Name,
                        param.Chain,
                        index,
                        walletAddress.Address);

                    var result = await ((IInOutBlockchainApi)Currency.BlockchainApi)
                        .GetOutputsAsync(walletAddress.Address, cancellationToken: cancellationToken)
                        .ConfigureAwait(false);

                    if (result.HasError)
                    {
                        Log.Error(
                            "Error while scan outputs for {@address} with code {@code} and description {@description}",
                            walletAddress.Address,
                            result.Error.Code,
                            result.Error.Description);
                        break;
                    }

                    var outputs = result.Value?.RemoveDuplicates().ToList();

                    if (outputs == null || !outputs.Any()) // address without activity
                    {
                        freeKeysCount++;

                        if (freeKeysCount >= param.LookAhead)
                        {
                            Log.Debug($"{param.LookAhead} free keys found. Chain scan completed");
                            break;
                        }
                    }
                    else // address has activity
                    {
                        freeKeysCount = 0;

                        await Account
                            .UpsertOutputsAsync(
                                outputs: outputs,
                                currency: Currency,
                                address: walletAddress.Address,
                                notifyIfBalanceUpdated: false)
                            .ConfigureAwait(false);
                    }

                    index++;
                }
            }
        }

        private async Task ScanTransactionsAsync(
            CancellationToken cancellationToken = default)
        {
            if (!Currency.IsTransactionsAvailable)
                return;

            var outputs = await Account
                .GetOutputsAsync(Currency)
                .ConfigureAwait(false);

            await ScanTransactionsAsync(outputs, cancellationToken)
                .ConfigureAwait(false);
        }

        private async Task ScanTransactionsAsync(
            IEnumerable<ITxOutput> outputs,
            CancellationToken cancellationToken = default)
        {
            foreach (var output in outputs)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var txIds = output.IsSpent
                    ? new[] { output.TxId, output.SpentTxPoint.Hash }
                    : new[] { output.TxId };

                foreach (var txId in txIds)
                {
                    var localTx = await Account
                        .GetTransactionByIdAsync(Currency, txId)
                        .ConfigureAwait(false);

                    // request only not confirmed transactions
                    if (localTx != null && localTx.IsConfirmed)
                        continue;

                    Log.Debug("Scan {@currency} transaction {@txId}", Currency.Name, txId);

                    var txResult = await Currency.BlockchainApi
                        .GetTransactionAsync(txId, cancellationToken)
                        .ConfigureAwait(false);

                    if (txResult.HasError)
                    {
                        Log.Error(
                            "Error while get transactions {@txId} with code {@code} and description {@description}",
                            txId,
                            txResult.Error.Code,
                            txResult.Error.Description);
                        continue;
                    }

                    var tx = txResult.Value;

                    if (tx == null)
                    {
                        Log.Warning("Wow! Transaction with id {@txId} not found", txId);
                        continue;
                    }

                    await Account
                        .UpsertTransactionAsync(
                            tx: tx,
                            updateBalance: false,
                            notifyIfUnconfirmed: false,
                            notifyIfBalanceUpdated: false,
                            cancellationToken: cancellationToken)
                        .ConfigureAwait(false);
                }
            }
        }
    }
}