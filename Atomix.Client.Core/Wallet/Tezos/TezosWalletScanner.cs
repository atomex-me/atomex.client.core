using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Atomix.Blockchain.Tezos;
using Atomix.Wallet.Abstract;
using Atomix.Wallet.Bip;
using Serilog;

namespace Atomix.Wallet.Tezos
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
            CancellationToken cancellationToken = default(CancellationToken))
        {
            var currency = Account.Currencies.Get<Atomix.Tezos>();

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

                    var transactions = (await((ITezosBlockchainApi)currency.BlockchainApi)
                        .GetTransactionsAsync(walletAddress.Address, cancellationToken: cancellationToken)
                        .ConfigureAwait(false))
                        .Cast<TezosTransaction>()
                        .ToList();

                    if (transactions.Count == 0) // address without activity
                    {
                        freeKeysCount++;

                        if (freeKeysCount >= param.LookAhead)
                        {
                            Log.Debug(
                                messageTemplate: "{@lookAhead} free keys found. Chain scan completed",
                                propertyValue: param.LookAhead);
                            break;
                        }
                    }
                    else // address has activity
                    {
                        freeKeysCount = 0;

                        await UpsertTransactionsAsync(transactions)
                            .ConfigureAwait(false);
                    }

                    index++;
                }
            }

            await Account
                .UpdateBalanceAsync(
                    currency: currency,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }

        public async Task ScanAsync(
            string address,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            var currency = Account.Currencies.Get<Atomix.Tezos>();

            Log.Debug(
                messageTemplate: "Scan transactions for address {@address}",
                propertyValue: address);

            var transactions = (await((ITezosBlockchainApi)currency.BlockchainApi)
                .GetTransactionsAsync(address, cancellationToken: cancellationToken)
                .ConfigureAwait(false))
                .Cast<TezosTransaction>()
                .ToList();

            if (transactions.Count == 0)
                return;

            await UpsertTransactionsAsync(transactions)
                .ConfigureAwait(false);

            await Account
                .UpdateBalanceAsync(
                    currency: currency,
                    address: address,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }

        private async Task UpsertTransactionsAsync(
            IEnumerable<TezosTransaction> transactions)
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