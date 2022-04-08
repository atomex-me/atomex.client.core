using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Serilog;

using Atomex.Blockchain;
using Atomex.Blockchain.Abstract;
using Atomex.Blockchain.Helpers;
using Atomex.Blockchain.Tezos;
using Atomex.Wallet.Abstract;
using Atomex.Wallet.Tezos;

namespace Atomex.Services
{
    public interface ITransactionTracker
    {

    }

    public class TransactionTracker : ITransactionTracker
    {
        protected static TimeSpan DefaultMaxTransactionTimeout = TimeSpan.FromMinutes(48 * 60);

        private readonly IAccount _account;

        private TimeSpan TransactionConfirmationCheckInterval(string currency) =>
            currency == "BTC"
                ? TimeSpan.FromSeconds(120)
                : TimeSpan.FromSeconds(45);

        public TransactionTracker(IAccount account)
        {
            _account = account ?? throw new ArgumentNullException(nameof(account));
            _account.UnconfirmedTransactionAdded += OnUnconfirmedTransactionAddedEventHandler;
        }

        public void Start()
        {
            // start async unconfirmed transactions tracking
            _ = TrackUnconfirmedTransactionsAsync(_cts.Token);
        }

        public void Stop()
        {
            // TODO:
        }

        private async Task TrackUnconfirmedTransactionsAsync(
            CancellationToken cancellationToken)
        {
            try
            {
                var txs = await _account
                    .GetTransactionsAsync()
                    .ConfigureAwait(false);

                foreach (var tx in txs)
                    if (!tx.IsConfirmed && tx.State != BlockchainTransactionState.Failed)
                        _ = TrackTransactionAsync(tx, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                Log.Debug("TrackUnconfirmedTransactionsAsync canceled");
            }
            catch (Exception e)
            {
                Log.Error(e, "Unconfirmed transactions track error.");
            }
        }

        private void OnUnconfirmedTransactionAddedEventHandler(object sender, TransactionEventArgs e)
        {
            if (!e.Transaction.IsConfirmed && e.Transaction.State != BlockchainTransactionState.Failed)
                _ = TrackTransactionAsync(e.Transaction, _cts.Token);
        }

        private Task TrackTransactionAsync(
            IBlockchainTransaction tx,
            CancellationToken cancellationToken)
        {
            return Task.Run(async () =>
            {
                try
                {
                    while (!cancellationToken.IsCancellationRequested)
                    {
                        var currency = _account.Currencies
                            .GetByName(tx.Currency);

                        var result = await currency
                            .IsTransactionConfirmed(
                                txId: tx.Id,
                                cancellationToken: cancellationToken)
                            .ConfigureAwait(false);

                        if (result.HasError)
                        {
                            await Task.Delay(TransactionConfirmationCheckInterval(tx?.Currency), cancellationToken)
                                .ConfigureAwait(false);

                            continue;
                        }

                        if (result.Value.IsConfirmed || result.Value.Transaction != null && result.Value.Transaction.State == BlockchainTransactionState.Failed)
                        {
                            TransactionProcessedHandler(result.Value.Transaction, cancellationToken);
                            break;
                        }

                        // mark old unconfirmed txs as failed
                        if (tx.CreationTime != null &&
                            DateTime.UtcNow > tx.CreationTime.Value.ToUniversalTime() + DefaultMaxTransactionTimeout &&
                            !Currencies.IsBitcoinBased(tx.Currency))
                        {
                            tx.State = BlockchainTransactionState.Failed;

                            TransactionProcessedHandler(tx, cancellationToken);
                            break;
                        }

                        await Task.Delay(TransactionConfirmationCheckInterval(tx?.Currency), cancellationToken)
                            .ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException)
                {
                    Log.Debug("TrackTransactionAsync canceled.");
                }
                catch (Exception e)
                {
                    Log.Error(e, "TrackTransactionAsync error.");
                }

            }, _cts.Token);
        }

        private async void TransactionProcessedHandler(
            IBlockchainTransaction tx,
            CancellationToken cancellationToken)
        {
            try
            {
                if (_account.GetCurrencyAccount(tx.Currency) is not ITransactionalAccount account)
                {
                    Log.Error("Transaction for {@currency} received.", tx.Currency);
                    return;
                }

                await account
                    .UpsertTransactionAsync(tx, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                await _account
                    .UpdateBalanceAsync(tx.Currency, cancellationToken)
                    .ConfigureAwait(false);

                if (Currencies.HasTokens(tx.Currency))
                    await UpdateTokenBalanceAsync(tx, cancellationToken)
                        .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                Log.Debug("Transaction processed handler task canceled.");
            }
            catch (Exception e)
            {
                Log.Error(e, "Error in transaction processed handler.");
            }
        }

        private async Task UpdateTokenBalanceAsync(
            IBlockchainTransaction tx,
            CancellationToken cancellationToken)
        {
            if (tx.Currency == EthereumConfig.Eth)
            {
                // 
            }
            else if (tx.Currency == TezosConfig.Xtz)
            {
                var tezosTx = tx as TezosTransaction;

                if (tezosTx.Params == null)
                    return;

                var tezosAccount = _account
                    .GetCurrencyAccount<TezosAccount>(TezosConfig.Xtz);

                var tezosTokensScanner = new TezosTokensScanner(tezosAccount);

                await tezosTokensScanner.ScanAsync(
                    skipUsed: false,
                    cancellationToken: cancellationToken);

                // reload balances for all tezos tokens account
                foreach (var currency in _account.Currencies)
                    if (Currencies.IsTezosToken(currency.Name))
                        _account.GetCurrencyAccount<TezosTokenAccount>(currency.Name)
                            .ReloadBalances();
            }
        }

        //private Task BalanceUpdateLoopAsync(CancellationToken cancellationToken)
        //{
        //    return Task.Run(async () =>
        //    {
        //        try
        //        {
        //            while (!cancellationToken.IsCancellationRequested)
        //            {
        //                await new HdWalletScanner(Account)
        //                    .ScanFreeAddressesAsync(cancellationToken)
        //                    .ConfigureAwait(false);

        //                await Task.Delay(TimeSpan.FromSeconds(Account.UserSettings.BalanceUpdateIntervalInSec), cancellationToken)
        //                    .ConfigureAwait(false);
        //            }
        //        }
        //        catch (OperationCanceledException)
        //        {
        //            Log.Debug("Balance autoupdate task canceled.");
        //        }
        //        catch (Exception e)
        //        {
        //            Log.Error(e, "Balance autoupdate task error");
        //        }
        //    });
        //}
    }
}