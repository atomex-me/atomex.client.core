using System;
using System.Threading;
using System.Threading.Tasks;

using Serilog;

using Atomex.Blockchain.Abstract;
using Atomex.Blockchain.Helpers;
using Atomex.Services.Abstract;
using Atomex.Wallet;
using Atomex.Wallet.Abstract;

namespace Atomex.Services
{
    public class TransactionsTracker : ITransactionsTracker
    {
        protected static TimeSpan DefaultMaxTransactionTimeout = TimeSpan.FromMinutes(48 * 60);

        private readonly IAccount _account;
        private readonly ILocalStorage _localStorage;
        private CancellationTokenSource _cts;

        public bool IsRunning { get; private set; }

        private TimeSpan TransactionConfirmationCheckInterval(string currency) =>
            currency == "BTC"
                ? TimeSpan.FromSeconds(120)
                : TimeSpan.FromSeconds(45);

        public TransactionsTracker(IAccount account, ILocalStorage localStorage)
        {
            _account = account ?? throw new ArgumentNullException(nameof(account));

            _localStorage = localStorage ?? throw new ArgumentNullException(nameof(localStorage));
            _localStorage.TransactionsChanged += LocalStorage_TransactionsChanged;
        }

        private void LocalStorage_TransactionsChanged(object sender, TransactionsChangedEventArgs e)
        {
            try
            {
                foreach (var tx in e.Transactions)
                {
                    if (!tx.IsConfirmed && tx.Status != TransactionStatus.Failed)
                        _ = TrackTransactionAsync(tx, _cts.Token);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "TransactionChanged event handler error");
            }
        }

        public void Start()
        {
            if (IsRunning)
                throw new InvalidOperationException("TransactionsTracker already running");

            IsRunning = true;

            _cts = new CancellationTokenSource();

            // start async unconfirmed transactions tracking
            Task.Run(async () =>
            {
                try
                {
                    var txs = await _account
                        .GetUnconfirmedTransactionsAsync()
                        .ConfigureAwait(false);

                    foreach (var tx in txs)
                    {
                        if (tx.Status != TransactionStatus.Failed)
                            _ = TrackTransactionAsync(tx, _cts.Token);
                    }
                }
                catch (OperationCanceledException)
                {
                    Log.Debug("TrackUnconfirmedTransactionsAsync canceled");
                }
                catch (Exception e)
                {
                    Log.Error(e, "Unconfirmed transactions track error");
                }

            }, _cts.Token);

            Log.Information("TransactionsTracker successfully started");
        }

        public void Stop()
        {
            if (!IsRunning)
                return;

            // unsubscribe from unconfirmed transaction added event handler
            _localStorage.TransactionsChanged -= LocalStorage_TransactionsChanged;

            // cancel all background tasks
            _cts.Cancel();

            Log.Information("TransactionsTracker stopped");

            IsRunning = false;
        }

        private Task TrackTransactionAsync(
            ITransaction tx,
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

                        var (result, error) = await currency
                            .IsTransactionConfirmed(
                                txId: tx.Id,
                                cancellationToken: cancellationToken)
                            .ConfigureAwait(false);

                        if (error != null)
                        {
                            await Task.Delay(TransactionConfirmationCheckInterval(tx?.Currency), cancellationToken)
                                .ConfigureAwait(false);

                            continue;
                        }

                        if (result.IsConfirmed || result.Transaction != null && result.Transaction.Status == TransactionStatus.Failed)
                        {
                            TransactionProcessedHandler(result.Transaction, cancellationToken);
                            break;
                        }

                        // mark old unconfirmed txs as failed
                        if (tx.CreationTime != null &&
                            DateTime.UtcNow > tx.CreationTime.Value.ToUniversalTime() + DefaultMaxTransactionTimeout &&
                            !Currencies.IsBitcoinBased(tx.Currency))
                        {
                            tx.Fail();

                            TransactionProcessedHandler(tx, cancellationToken);
                            break;
                        }

                        await Task.Delay(TransactionConfirmationCheckInterval(tx?.Currency), cancellationToken)
                            .ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException)
                {
                    Log.Debug("TrackTransactionAsync canceled");
                }
                catch (Exception e)
                {
                    Log.Error(e, "TrackTransactionAsync error");
                }

            }, _cts.Token);
        }

        private async void TransactionProcessedHandler(
            ITransaction tx,
            CancellationToken cancellationToken)
        {
            try
            {
                await _localStorage
                    .UpsertTransactionAsync(tx, notifyIfNewOrChanged: true, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                Log.Debug("Transaction processed handler task canceled");
            }
            catch (Exception e)
            {
                Log.Error(e, "Error in transaction processed handler");
            }
        }

        //private async Task UpdateTokenBalanceAsync(
        //    IBlockchainTransaction tx,
        //    CancellationToken cancellationToken)
        //{
        //    if (tx.Currency == EthereumConfig.Eth)
        //    {
        //        // 
        //    }
        //    else if (tx.Currency == TezosConfig.Xtz)
        //    {
        //        var tezosTx = tx as TezosTransaction;

        //        if (tezosTx.Params == null)
        //            return;

        //        var tezosAccount = _account
        //            .GetCurrencyAccount<TezosAccount>(TezosConfig.Xtz);

        //        var tezosTokensScanner = new TezosTokensWalletScanner(tezosAccount);

        //        // todo: scan balance only for addresses affected by transaction!!
        //        await tezosTokensScanner
        //            .UpdateBalanceAsync(cancellationToken)
        //            .ConfigureAwait(false);
        //    }
        //}
    }
}