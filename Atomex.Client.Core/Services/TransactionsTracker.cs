using System;
using System.Threading;
using System.Threading.Tasks;

using Serilog;

using Atomex.Blockchain;
using Atomex.Blockchain.Abstract;
using Atomex.Blockchain.Helpers;
using Atomex.Blockchain.Tezos;
using Atomex.Wallet.Abstract;
using Atomex.Wallet.Tezos;
using Atomex.Services.Abstract;

namespace Atomex.Services
{
    public class TransactionsTracker : ITransactionsTracker
    {
        protected static TimeSpan DefaultMaxTransactionTimeout = TimeSpan.FromMinutes(48 * 60);

        private readonly IAccount_OLD _account;
        private CancellationTokenSource _cts;

        public bool IsRunning { get; private set; }

        private TimeSpan TransactionConfirmationCheckInterval(string currency) =>
            currency == "BTC"
                ? TimeSpan.FromSeconds(120)
                : TimeSpan.FromSeconds(45);

        public TransactionsTracker(IAccount_OLD account)
        {
            _account = account ?? throw new ArgumentNullException(nameof(account));
            _account.UnconfirmedTransactionAdded += OnUnconfirmedTransactionAddedEventHandler;
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
                        .GetTransactionsAsync()
                        .ConfigureAwait(false);

                    foreach (var tx in txs)
                        if (!tx.IsConfirmed && tx.State != BlockchainTransactionState.Failed)
                            _ = TrackTransactionAsync(tx, _cts.Token);
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
            _account.UnconfirmedTransactionAdded -= OnUnconfirmedTransactionAddedEventHandler;

            // cancel all background tasks
            _cts.Cancel();

            Log.Information("TransactionsTracker stopped");

            IsRunning = false;
        }

        private void OnUnconfirmedTransactionAddedEventHandler(object sender, TransactionEventArgs e)
        {
            if (!e.Transaction.IsConfirmed && e.Transaction.State != BlockchainTransactionState.Failed)
                _ = TrackTransactionAsync(e.Transaction, _cts.Token);
        }

        private Task TrackTransactionAsync(
            IBlockchainTransaction_OLD tx,
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
                    Log.Debug("TrackTransactionAsync canceled");
                }
                catch (Exception e)
                {
                    Log.Error(e, "TrackTransactionAsync error");
                }

            }, _cts.Token);
        }

        private async void TransactionProcessedHandler(
            IBlockchainTransaction_OLD tx,
            CancellationToken cancellationToken)
        {
            try
            {
                if (_account.GetCurrencyAccount(tx.Currency) is not ITransactionalAccount account)
                {
                    Log.Error("Transaction for {@currency} received", tx.Currency);
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
                Log.Debug("Transaction processed handler task canceled");
            }
            catch (Exception e)
            {
                Log.Error(e, "Error in transaction processed handler");
            }
        }

        private async Task UpdateTokenBalanceAsync(
            IBlockchainTransaction_OLD tx,
            CancellationToken cancellationToken)
        {
            if (tx.Currency == EthereumConfig_ETH.Eth)
            {
                // 
            }
            else if (tx.Currency == TezosConfig_OLD.Xtz)
            {
                var tezosTx = tx as TezosTransaction_OLD;

                if (tezosTx.Params == null)
                    return;

                var tezosAccount = _account
                    .GetCurrencyAccount<TezosAccount>(TezosConfig_OLD.Xtz);

                var tezosTokensScanner = new TezosTokensScanner_OLD(tezosAccount);

                await tezosTokensScanner.ScanAsync(
                    skipUsed: false,
                    cancellationToken: cancellationToken);

                // reload balances for all tezos tokens account
                foreach (var currency in _account.Currencies)
                    if (Currencies.IsTezosToken(currency.Name))
                        _account.GetCurrencyAccount<TezosTokenAccount_OLD>(currency.Name)
                            .ReloadBalances();
            }
        }
    }
}