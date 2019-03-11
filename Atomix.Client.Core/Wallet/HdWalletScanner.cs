using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Atomix.Blockchain.Abstract;
using Atomix.Blockchain.Ethereum;
using Atomix.Core.Entities;
using Atomix.Wallet.Abstract;
using Atomix.Wallet.Bip;
using Serilog;

namespace Atomix.Wallet
{
    public class HdWalletScanner : IHdWalletScanner
    {
        public const int DefaultInternalLookAhead = 3;
        public const int DefaultExternalLookAhead = 3;

        public int InternalLookAhead { get; set; } = DefaultInternalLookAhead;
        public int ExternalLookAhead { get; set; } = DefaultExternalLookAhead;
        public IAccount Account { get; }

        //public HashSet<AddressIndex> UsedAddressIndices { get; }

        public HdWalletScanner(IAccount account)
        {
            Account = account ?? throw new ArgumentNullException(nameof(account));

            //UsedAddressIndices = new HashSet<AddressIndex>();
        }

        public async Task ScanAsync(
            bool skipUsed = false,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            foreach (var currency in Account.Currencies)
                if (currency.IsTransactionsAvailable)
                    await ScanAsync(currency, skipUsed, cancellationToken)
                        .ConfigureAwait(false);
        }

        public async Task ScanAsync(
            Currency currency,
            bool skipUsed = false,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            if (currency is Ethereum)
            {
                await ScanTransactionsAsync(currency, cancellationToken)
                    .ConfigureAwait(false);
            }
            else
            {
                await ScanOutputsAsync(
                        currency: currency,
                        skipUsed: skipUsed,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                await ScanTransactionsAsync(currency, cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        private async Task ScanOutputsAsync(
            Currency currency,
            bool skipUsed,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            Log.Debug("Scan outputs for {@name}", currency.Name);

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

                    var walletAddress = Account.GetAddress(currency, param.Chain, index);

                    if (skipUsed) // check, if the address marked as "used" and skip in this case
                    {
                        var addressOutputs = (await Account
                            .GetOutputsAsync(currency, walletAddress.Address)
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

                    Log.Debug("Scan outputs for {@name} address {@address}", currency.Name, walletAddress.Address);

                    var outputs = (await ((IInOutBlockchainApi)currency.BlockchainApi)
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
                                currency: currency,
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
            Currency currency,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            if (currency is Ethereum)
            {
                await ScanEthereumTransactionsAsync(currency, cancellationToken);
                return;
            }

            if (!currency.IsTransactionsAvailable)
                return;

            var outputs = await Account
                .GetOutputsAsync(currency)
                .ConfigureAwait(false);

            foreach (var output in outputs)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var txIds = output.IsSpent
                    ? new[] { output.TxId, output.SpentTxPoint.Hash }
                    : new[] { output.TxId };

                foreach (var txId in txIds)
                {
                    var tx = await Account
                        .GetTransactionByIdAsync(currency, txId)
                        .ConfigureAwait(false);

                    // request only not confirmed transactions
                    if (tx != null && tx.IsConfirmed())
                        continue;

                    var transaction = await currency.BlockchainApi
                        .GetTransactionAsync(txId, cancellationToken)
                        .ConfigureAwait(false);

                    await Account
                        .AddTransactionAsync(transaction)
                        .ConfigureAwait(false);
                }
            }
        }

        private async Task ScanEthereumTransactionsAsync(
            Currency currency,
            CancellationToken cancellationToken = default(CancellationToken))
        {
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

                    var walletAddress = Account.GetAddress(currency, param.Chain, index);

                    Log.Debug("Scan transactions for {@name} address {@address}", currency.Name, walletAddress.Address);

                    var transactions = (await ((IEthereumBlockchainApi)currency.BlockchainApi)
                        .GetTransactionsAsync(walletAddress.Address, cancellationToken: cancellationToken)
                        .ConfigureAwait(false))
                        .Cast<EthereumTransaction>()
                        .ToList();

                    if (transactions.Count == 0) // address without activity
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

                        foreach (var tx in transactions)
                        {
                            var txId = tx.IsInternal ? tx.Id + "-internal" : tx.Id;

                            var existsTx = (EthereumTransaction)await Account
                                .GetTransactionByIdAsync(currency, txId)
                                .ConfigureAwait(false);

                            if (existsTx != null &&
                                existsTx.Type != tx.Type &&
                                existsTx.Type != EthereumTransaction.UnknownTransaction)
                            {
                                tx.Type = EthereumTransaction.SelfTransaction;
                            }

                            await Account
                                .AddTransactionAsync(tx)
                                .ConfigureAwait(false);
                        }
                    }

                    index++;
                }
            }
        }
    }
}