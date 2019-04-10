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
        private const int DefaultInternalLookAhead = 1;
        private const int DefaultExternalLookAhead = 1;

        public int InternalLookAhead { get; set; } = DefaultInternalLookAhead;
        public int ExternalLookAhead { get; set; } = DefaultExternalLookAhead;
        public IAccount Account { get; }

        public TezosWalletScanner(IAccount account)
        {
            Account = account ?? throw new ArgumentNullException(nameof(account));
        }

        public async Task ScanAsync(
            bool skipUsed = false,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            var currency = Currencies.Xtz;

            ///var scanParams = new[]
            //{
            //    new {Chain = Bip44.Internal, LookAhead = InternalLookAhead},
            //    new {Chain = Bip44.External, LookAhead = ExternalLookAhead},
           // };

            //foreach (var param in scanParams)
            //{
                //var freeKeysCount = 0;
                //var index = 0u;

                //while (true)
                //{
                    cancellationToken.ThrowIfCancellationRequested();

                    var walletAddress = Account.GetAddress(currency, Bip44.External, 0); //param.Chain, index);

                    Log.Debug(
                        "Scan transactions for {@name} address {@address}",
                        currency.Name,
                        walletAddress.Address);

                    var transactions = (await((ITezosBlockchainApi)currency.BlockchainApi)
                        .GetTransactionsAsync(walletAddress.Address, cancellationToken: cancellationToken)
                        .ConfigureAwait(false))
                        .Cast<TezosTransaction>()
                        .ToList();

                    if (transactions.Count == 0) // address without activity
                    {
                        //freeKeysCount++;

                        //if (freeKeysCount >= param.LookAhead)
                        //{
                        //    Log.Debug($"{param.LookAhead} free keys found. Chain scan completed");
                        //    break;
                        //}
                    }
                    else // address has activity
                    {
                        //freeKeysCount = 0;
                        await ScanTransactionsAsync(transactions, cancellationToken)
                            .ConfigureAwait(false);
                    }

                    //index++;
                //}
            //}
        }

        public async Task ScanAsync(
            string address,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            var currency = Currencies.Xtz;

            Log.Debug(
                "Scan transactions for address {@address}",
                address);

            var transactions = (await((ITezosBlockchainApi)currency.BlockchainApi)
                .GetTransactionsAsync(address, cancellationToken: cancellationToken)
                .ConfigureAwait(false))
                .Cast<TezosTransaction>()
                .ToList();

            if (transactions.Count == 0)
                return;

            await ScanTransactionsAsync(transactions, cancellationToken)
                .ConfigureAwait(false);
        }

        private async Task ScanTransactionsAsync(
            IEnumerable<TezosTransaction> transactions,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            var currency = Currencies.Eth;

            foreach (var tx in transactions)
            {
                var txId = tx.IsInternal ? tx.Id + "-internal" : tx.Id;

                var existsTx = (TezosTransaction)await Account
                    .GetTransactionByIdAsync(currency, txId)
                    .ConfigureAwait(false);

                if (existsTx != null &&
                    existsTx.Type != tx.Type)
                {
                    tx.Type = TezosTransaction.SelfTransaction;
                }

                await Account
                    .AddTransactionAsync(tx)
                    .ConfigureAwait(false);
            }
        }
    }
}