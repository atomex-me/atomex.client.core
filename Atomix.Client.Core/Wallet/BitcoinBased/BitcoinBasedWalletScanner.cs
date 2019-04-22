using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Atomix.Blockchain.Abstract;
using Atomix.Core.Entities;
using Atomix.Wallet.Abstract;
using Atomix.Wallet.Bip;
using Serilog;

namespace Atomix.Wallet.BitcoinBased
{
    public class BitcoinBasedWalletScanner : ICurrencyHdWalletScanner
    {
        private const int DefaultInternalLookAhead = 3;
        private const int DefaultExternalLookAhead = 3;

        public Currency Currency { get; set; }
        public int InternalLookAhead { get; set; } = DefaultInternalLookAhead;
        public int ExternalLookAhead { get; set; } = DefaultExternalLookAhead;
        public IAccount Account { get; }

        public BitcoinBasedWalletScanner(Currency currency, IAccount account)
        {
            Currency = currency ?? throw new ArgumentNullException(nameof(currency));
            Account = account ?? throw new ArgumentNullException(nameof(account));
        }

        public async Task ScanAsync(
            bool skipUsed = false,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            await ScanOutputsAsync(
                    skipUsed: skipUsed,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            await ScanTransactionsAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        private async Task ScanOutputsAsync(
           bool skipUsed,
           CancellationToken cancellationToken = default(CancellationToken))
        {
            Log.Debug("Scan outputs for {@name}", Currency.Name);

            var scanParams = new[]
            {
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

                    var walletAddress = Account.GetAddress(Currency, param.Chain, index);

                    if (skipUsed) // check, if the address marked as "used" and skip in this case
                    {
                        var addressOutputs = (await Account
                            .GetOutputsAsync(Currency, walletAddress.Address)
                            .ConfigureAwait(false))
                            .ToList();

                        if (addressOutputs.Any() && addressOutputs.FirstOrDefault(o => !o.IsSpent) == null)
                        {
                            //UsedAddressIndices.Add(new AddressIndex(param.Chain, index));
                            freeKeysCount = 0;
                            index++;

                            continue;
                        }
                    }

                    Log.Debug("Scan outputs for {@name} address {@address}", Currency.Name, walletAddress.Address);

                    var outputs = (await ((IInOutBlockchainApi)Currency.BlockchainApi)
                        .GetOutputsAsync(walletAddress.Address, cancellationToken: cancellationToken)
                        .ConfigureAwait(false))
                        .ToList();

                    if (outputs.Count == 0) // address without activity
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

                        //var unspent = outputs
                        //    .Where(output => !output.IsSpent)
                        //    .Sum(output => output.Value);

                        //if (unspent == 0) // all funds spent, address can be marked as used
                        //    UsedAddressIndices.Add(new AddressIndex(param.Chain, index));

                        await Account
                            .AddOutputsAsync(
                                outputs: outputs.GroupBy(o => $"{o.TxId}{o.Index}", RemoveDuplicates),
                                currency: Currency,
                                address: walletAddress.Address)
                            .ConfigureAwait(false);
                    }

                    index++;
                }
            }
        }

        private static ITxOutput RemoveDuplicates(string id, IEnumerable<ITxOutput> outputs)
        {
            var txOutputs = outputs.ToList();

            return txOutputs.Count == 1
                ? txOutputs.First()
                : txOutputs.First(o => o.IsSpent);
        }

        private async Task ScanTransactionsAsync(
            CancellationToken cancellationToken = default(CancellationToken))
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
            CancellationToken cancellationToken = default(CancellationToken))
        {
            foreach (var output in outputs)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var txIds = output.IsSpent
                    ? new[] { output.TxId, output.SpentTxPoint.Hash }
                    : new[] { output.TxId };

                foreach (var txId in txIds)
                {
                    var tx = await Account
                        .GetTransactionByIdAsync(Currency, txId)
                        .ConfigureAwait(false);

                    // request only not confirmed transactions
                    if (tx != null && tx.IsConfirmed())
                        continue;

                    var transaction = await Currency.BlockchainApi
                        .GetTransactionAsync(txId, cancellationToken)
                        .ConfigureAwait(false);

                    if (transaction == null) {
                        Log.Warning("Wow! Transaction with id {@id} not found", txId);
                        continue;
                    }

                    await Account
                        .UpdateTransactionType(transaction, cancellationToken)
                        .ConfigureAwait(false);

                    await Account
                        .AddTransactionAsync(transaction)
                        .ConfigureAwait(false);
                }
            }
        }

        public async Task ScanAsync(
            string address,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            Log.Debug("Scan outputs for {@address}", address);

            var outputs = (await((IInOutBlockchainApi)Currency.BlockchainApi)
                .GetOutputsAsync(address, cancellationToken: cancellationToken)
                .ConfigureAwait(false))
                .ToList();

            if (outputs.Count == 0)
                return;

            await Account
                .AddOutputsAsync(
                    outputs: outputs.GroupBy(o => $"{o.TxId}{o.Index}", RemoveDuplicates),
                    currency: Currency,
                    address: address)
                .ConfigureAwait(false);

            await ScanTransactionsAsync(outputs, cancellationToken)
                .ConfigureAwait(false);         
        }
    }
}