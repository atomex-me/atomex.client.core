using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Serilog;

using Atomex.Blockchain.Tezos;
using Atomex.Common;
using Atomex.Wallets.Tezos.Common;
using Atomex.Wallet.Tezos;

namespace Atomex.Wallets.Tezos
{
    public class TezosAddressOperatioPerBlockManager : IDisposable
    {
        private const int ConfirmationCheckIntervalSec = 5;

        private readonly TezosAccount _account;
        private readonly AsyncQueue<TezosOperationGroup> _operationsQueue;
        private CancellationTokenSource _cts;
        private Task _worker;
        
        private readonly ManualResetEventAsync _confirmedEvent;
        private bool _disposedValue;

        public bool IsRunning => _worker != null &&
            !_worker.IsCompleted &&
            !_worker.IsCanceled &&
            !_worker.IsFaulted;

        public TezosAddressOperatioPerBlockManager(TezosAccount account)
        {
            _account = account ?? throw new ArgumentNullException(nameof(account));
            _operationsQueue = new AsyncQueue<TezosOperationGroup>();
            _confirmedEvent = new ManualResetEventAsync(isSet: false);
        }

        public async Task<Result<TezosOperation>> SendOperationAsync(
            IEnumerable<TezosOperationParameters> operationsParameters,
            CancellationToken cancellationToken = default)
        {
            var operationGroup = new TezosOperationGroup(operationsParameters);

            _operationsQueue.Add(operationGroup);

            // wait for completion
            await operationGroup
                .CompletionEvent
                .WaitAsync(cancellationToken)
                .ConfigureAwait(false);

            if (operationGroup.Error != null)
                return operationGroup.Error;

            return operationGroup.Operation;
        }

        public void Start()
        {
            if (IsRunning)
                return;

            _cts = new CancellationTokenSource();
            _worker = Task.Run(DoWork, _cts.Token);
        }

        public void Stop()
        {
            if (IsRunning)
                _cts.Cancel();
        }

        public void NotifyConfirmation()
        {
            _confirmedEvent.Set();
            _confirmedEvent.Reset();
        }

        private async Task DoWork()
        {
            List<TezosOperationGroup> operationGroups = null;

            while (!_cts.IsCancellationRequested)
            {
                operationGroups?.Clear();

                try
                {
                    // check if there are unconfirmed operations
                    var unconfirmedOperations = await _account
                        .LocalStorage
                        .GetUnconfirmedTransactionsAsync<TezosOperationRequest>(
                            currency: _account.Currency,
                            cancellationToken: _cts.Token)
                        .ConfigureAwait(false);

                    while (unconfirmedOperations.Any())
                    {
                        // wait for external confirmation event or ConfirmationCheckIntervalSec timeout
                        _ = await _confirmedEvent
                            .WaitAsync(TimeSpan.FromSeconds(ConfirmationCheckIntervalSec), _cts.Token)
                            .ConfigureAwait(false);

                        // check again if there are unconfirmed operations
                        unconfirmedOperations = await _account
                            .LocalStorage
                            .GetUnconfirmedTransactionsAsync<TezosOperation>(
                                currency: _account.Currency,
                                cancellationToken: _cts.Token)
                            .ConfigureAwait(false);
                    }

                    // get all operation groups from the queue
                    operationGroups = await GetOperationGroupsFromQueueAsync(_operationsQueue, _cts.Token)
                        .ConfigureAwait(false);

                    var operationsParameters = new List<TezosOperationParameters>();

                    foreach (var og in operationGroups)
                        operationsParameters.AddRange(og.OperationsParameters);

                    // fill operation
                    var (operation, fillingError) = await TezosOperationFiller
                        .FillOperationAsync(
                            operationsRequests: operationsParameters,
                            account: _account,
                            cancellationToken: _cts.Token)
                        .ConfigureAwait(false);

                    var (txId, sendingError) = await _account
                        .SendOperationAsync(operation, _cts.Token)
                        .ConfigureAwait(false);

                    if (sendingError != null)
                    {
                        // notify all subscribers about error
                        foreach (var op in operationGroups)
                            op.CompleteWithError(sendingError);

                        continue;
                    }

                    // notify all subscribers
                    foreach (var op in operationGroups)
                        op.CompleteWithOperation(operation);
                }
                catch (OperationCanceledException)
                {
                    // nothing to do
                }
                catch (Exception ex)
                {
                    Log.Error($"Operation batching error: {ex.Message}");

                    var error = new Error(Errors.OperationBatchingError, ex.Message);

                    // notify all subscribers about error
                    if (operationGroups != null)
                        foreach (var op in operationGroups)
                            op.CompleteWithError(error);
                }
            }
        }

        public static async Task<List<TezosOperationGroup>> GetOperationGroupsFromQueueAsync(
            AsyncQueue<TezosOperationGroup> queue,
            CancellationToken cancellationToken)
        {
            var operationGroups = new List<TezosOperationGroup>();

            do
            {
                var op = await queue
                    .TakeAsync(cancellationToken)
                    .ConfigureAwait(false);

                operationGroups.Add(op);

            } while (queue.Count > 0);

            return operationGroups;
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposedValue)
            {
                if (disposing)
                {
                    Stop();

                    _operationsQueue?.Clear();
                }

                _disposedValue = true;
            }
        }

        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }

    public class TezosOperationPerBlockManager : IDisposable
    {
        private readonly ConcurrentDictionary<string, Lazy<TezosAddressOperatioPerBlockManager>> _batchers;
        private bool _disposedValue;

        public TezosOperationPerBlockManager()
        {
            _batchers = new ConcurrentDictionary<string, Lazy<TezosAddressOperatioPerBlockManager>>();
        }

        public async Task<Result<TezosOperation>> SendOperationsAsync(
            TezosAccount account,
            IEnumerable<TezosOperationParameters> operationsParameters,
            CancellationToken cancellationToken = default)
        {
            var from = operationsParameters.First().From;

            var lazyBatcher = _batchers.GetOrAdd(
                key: from,
                valueFactory: a => new Lazy<TezosAddressOperatioPerBlockManager>(
                    () => new TezosAddressOperatioPerBlockManager(account)));

            var batcher = lazyBatcher.Value;

            if (!batcher.IsRunning)
                batcher.Start();

            return await batcher
                .SendOperationAsync(
                    operationsParameters: operationsParameters,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposedValue)
            {
                if (disposing)
                {
                    foreach (var batcher in _batchers.Values)
                    {
                        batcher.Value?.Stop();
                        batcher.Value?.Dispose();
                    }

                    _batchers.Clear();
                }

                _disposedValue = true;
            }
        }

        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}