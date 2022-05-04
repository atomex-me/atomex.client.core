using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;

using Atomex.Blockchain.Bitcoin;
using Atomex.Blockchain.Bitcoin.Abstract;
using Atomex.Common;
using Atomex.Wallets.Abstract;
using Atomex.Wallets.Common;

namespace Atomex.Wallets.Bitcoin
{
    public class BitcoinWalletScanner : WalletScanner<IBitcoinApi>
    {
        /// <summary>
        /// Collection of outputs detected during scanning
        /// </summary>
        public List<BitcoinTxOutput> ChangedOutputs { get; } =
            new List<BitcoinTxOutput>();

        private BitcoinAccount Account => _account as BitcoinAccount;

        public BitcoinWalletScanner(
            BitcoinAccount account,
            IWalletProvider walletProvider,
            ILogger logger = null)
            : base(account, walletProvider, logger)
        {
        }

        public Task<Error> ScanTransactionsAsync(
            IEnumerable<BitcoinTxOutput> outputs,
            CancellationToken cancellationToken = default)
        {
            return Task.Run<Error>(async () =>
            {
                var api = GetBlockchainApi();

                foreach (var output in outputs)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var txIds = new List<string> { output.TxId };

                    if (output.SpentTxPoints?.Any() ?? false)
                        txIds.AddRange(output.SpentTxPoints.Select(p => p.Hash));

                    foreach (var txId in txIds)
                    {
                        var localTx = await Account
                            .GetTransactionByIdAsync<BitcoinTransaction>(
                                txId: txId,
                                cancellationToken: cancellationToken)
                            .ConfigureAwait(false);

                        if (localTx != null && localTx.Confirmations < 3)
                            continue;

                        _logger.LogInformation("[{currency}] Scan transaction {txid}",
                            Account.Currency,
                            txId);

                        var (tx, error) = await api
                            .GetTransactionAsync(txId, cancellationToken)
                            .ConfigureAwait(false);

                        if (error != null)
                        {
                            _logger.LogError(error.ToString());
                            continue;
                        }

                        if (tx == null)
                        {
                            _logger.LogError("[{currency}] Null tx response for {txId}",
                                Account.Currency,
                                txId);

                            continue;
                        }

                        var upsertResult = await Account
                            .UpsertTransactionAsync(
                                tx: tx as BitcoinTransaction,
                                cancellationToken: cancellationToken)
                            .ConfigureAwait(false);
                    }
                }

                return null; // no errors

            }, cancellationToken);
        }

        protected override async Task<(bool hasActivity, Error error)> UpdateAddressBalanceAsync(
            string address,
            string keyPath,
            WalletInfo walletInfo,
            WalletAddress storedAddress,
            IBitcoinApi api,
            CancellationToken cancellationToken = default)
        {
            var (outputs, error) = await api
                .GetOutputsAsync(address, cancellationToken)
                .ConfigureAwait(false);

            if (error != null)
            {
                _logger.LogError("[{currency}] Error while scan outputs for {address}",
                    Account.Currency,
                    address);

                return (hasActivity: false, error);
            }

            var outputsCount = outputs?.Count() ?? 0;

            var hasActivity = outputsCount > 0;

            if (hasActivity)
            {
                // todo: upsert only changed outputs

                var _ = await Account
                    .UpsertOutputsAsync(outputs, cancellationToken)
                    .ConfigureAwait(false);

                // save the outputs to be able to scan transactions later
                ChangedOutputs.AddRange(outputs);
            }

            var addressBalance = hasActivity
                ? CalculateBalance(outputs)
                : Balance.Zero;

            await Account
                .UpsertAddressAsync(
                    walletAddress: new WalletAddress
                    {
                        Currency    = Account.Currency,
                        Address     = address,
                        Balance     = addressBalance,
                        WalletId    = walletInfo.Id,
                        KeyPath     = keyPath,
                        KeyIndex    = !walletInfo.IsSingleKeyWallet
                            ? keyPath.GetIndex(walletInfo.KeyPathPattern, KeyPathExtensions.IndexPattern)
                            : 0,
                        HasActivity = hasActivity,
                        Counter     = outputsCount
                    }, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            return (hasActivity, error: null); // no errors
        }

        protected override CurrencyConfig GetCurrencyConfig() =>
            Account.Configuration;

        protected override IBitcoinApi GetBlockchainApi() => new BitcoinApi(
            settings: Account.Configuration.ApiSettings,
            logger: _logger);

        private Balance CalculateBalance(IEnumerable<BitcoinTxOutput> outputs)
        {
            var balance = new Balance(0, 0, 0, 0, 0, DateTime.UtcNow);

            var digitsMultiplier = Account.Configuration.DecimalsMultiplier;

            foreach (var output in outputs)
            {
                var value = output.Value / digitsMultiplier;

                if (output.IsConfirmed) {
                    balance.Received += value;
                } else {
                    balance.UnconfirmedIncome += value;
                }

                if (output.IsSpentConfirmed) {
                    balance.Sent += value;
                } else if (output.IsSpent) {
                    balance.UnconfirmedOutcome += value;
                }
            }

            balance.Total = balance.Received - balance.Sent;

            return balance;
        }
    }
}