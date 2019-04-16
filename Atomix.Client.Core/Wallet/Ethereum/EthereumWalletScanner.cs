using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Atomix.Blockchain.Ethereum;
using Atomix.Wallet.Abstract;
using Atomix.Wallet.Bip;
using Serilog;

namespace Atomix.Wallet.Ethereum
{
    public class EthereumWalletScanner : ICurrencyHdWalletScanner
    {
        private const int DefaultInternalLookAhead = 3;
        private const int DefaultExternalLookAhead = 3;

        public int InternalLookAhead { get; set; } = DefaultInternalLookAhead;
        public int ExternalLookAhead { get; set; } = DefaultExternalLookAhead;
        public IAccount Account { get; }

        public EthereumWalletScanner(IAccount account)
        {
            Account = account ?? throw new ArgumentNullException(nameof(account));
        }

        public async Task ScanAsync(
            bool skipUsed = false,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            var currency = Currencies.Eth;

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

                    var transactions = (await((IEthereumBlockchainApi)currency.BlockchainApi)
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

                        await AddTransactionsAsync(transactions, cancellationToken)
                            .ConfigureAwait(false);
                    }

                    index++;
                }
            }
        }

        public async Task ScanAsync(
            string address,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            var currency = Currencies.Eth;

            Log.Debug("Scan transactions for  address {@address}", address);

            var transactions = (await((IEthereumBlockchainApi)currency.BlockchainApi)
                .GetTransactionsAsync(address, cancellationToken: cancellationToken)
                .ConfigureAwait(false))
                .Cast<EthereumTransaction>()
                .ToList();

            if (transactions.Count == 0)
                return;

            await AddTransactionsAsync(transactions, cancellationToken)
                .ConfigureAwait(false);
        }

        private async Task AddTransactionsAsync(
            IEnumerable<EthereumTransaction> transactions,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            var currency = Currencies.Eth;

            foreach (var tx in transactions)
            {
                await Account
                    .UpdateTransactionType(tx, cancellationToken)
                    .ConfigureAwait(false);

                var isNewOrChanged = await IsTransactionNewOrChangedAsync(tx, cancellationToken)
                    .ConfigureAwait(false);

                if (isNewOrChanged)
                    await Account
                        .AddTransactionAsync(tx)
                        .ConfigureAwait(false);
            }
        }

        private async Task<bool> IsTransactionNewOrChangedAsync(
            EthereumTransaction tx,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            var txId = tx.IsInternal ? tx.Id + "-internal" : tx.Id;

            var existsTx = await Account.GetTransactionByIdAsync(Currencies.Eth, txId)
                .ConfigureAwait(false) as EthereumTransaction;

            return existsTx == null ||
                   existsTx.IsConfirmed() != tx.IsConfirmed() ||
                   existsTx.Type != tx.Type ||
                   existsTx.BlockInfo.Fees != tx.BlockInfo.Fees ||
                   existsTx.BlockInfo.FirstSeen != tx.BlockInfo.FirstSeen;
        }
    }
}