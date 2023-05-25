using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;

using Atomex.Blockchain.Tezos;
using Atomex.Common;
using Atomex.Wallets.Tezos.Common;
using Atomex.Wallet.Tezos;

namespace Atomex.Wallets.Tezos
{
    public class TezosAddressOperationBatcher : IDisposable
    {
        private class TezosOperationCompletionEvent
        {
            public TezosOperationCompletionEvent(IEnumerable<TezosOperationParameters> operationsParameters)
            {
                OperationsParameters = operationsParameters ?? throw new ArgumentNullException(nameof(operationsParameters));
                CompletionEvent = new ManualResetEventAsync(isSet: false);
            }

            public IEnumerable<TezosOperationParameters> OperationsParameters { get; }
            public ManualResetEventAsync CompletionEvent { get; }
            public TezosOperationRequestResult Result { get; private set; }

            public void CompleteWithResult(TezosOperationRequestResult result)
            {
                Result = result;
                CompletionEvent.Set();
            }
        }


        private const int ConfirmationCheckIntervalSec = 5;
        private const int ConfirmationWaitingTimeOutInSec = 60;

        private readonly Action<string> _onComplete;
        private readonly string _address;
        private readonly TezosAccount _account;
        private readonly AsyncQueue<TezosOperationCompletionEvent> _operationsQueue;
        private CancellationTokenSource _cts;
        private Task _worker;
        private readonly ILogger _logger;

        private readonly ManualResetEventAsync _confirmedEvent;
        private bool _disposedValue;

        public bool IsRunning => _worker != null &&
            !_worker.IsCompleted &&
            !_worker.IsCanceled &&
            !_worker.IsFaulted;

        public TezosAddressOperationBatcher(
            string address,
            TezosAccount account,
            Action<string> onComplete,
            ILogger logger = null)
        {
            _address = address ?? throw new ArgumentNullException(nameof(address));
            _account = account ?? throw new ArgumentNullException(nameof(account));
            _onComplete = onComplete;
            _operationsQueue = new AsyncQueue<TezosOperationCompletionEvent>();
            _confirmedEvent = new ManualResetEventAsync(isSet: false);
            _logger = logger;
        }

        public async Task<Result<TezosOperationRequestResult>> SendOperationAsync(
            IEnumerable<TezosOperationParameters> operationsParameters,
            CancellationToken cancellationToken = default)
        {
            var operationCompletionEvent = new TezosOperationCompletionEvent(operationsParameters);

            _logger?.LogDebug($"Enqueue operation");

            _operationsQueue.Add(operationCompletionEvent);

            // wait for completion
            await operationCompletionEvent
                .CompletionEvent
                .WaitAsync(cancellationToken)
                .ConfigureAwait(false);

            return operationCompletionEvent.Result;
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
            List<TezosOperationCompletionEvent> operationCompletionEvents = null;

            while (!_cts.IsCancellationRequested)
            {
                operationCompletionEvents?.Clear();

                try
                {
                    // check if there are unconfirmed operations
                    var unconfirmedOperations = await _account
                        .LocalStorage
                        .GetUnconfirmedTransactionsAsync<TezosOperation>(
                            currency: _account.Currency)
                        .ConfigureAwait(false);

                    while (unconfirmedOperations.Any(o => DateTimeOffset.UtcNow - o.CreationTime < TimeSpan.FromSeconds(ConfirmationWaitingTimeOutInSec)))
                    {
                        _logger?.LogDebug("Waiting for confirmation of unconfirmed transactions");

                        // wait for external confirmation event or ConfirmationCheckIntervalSec timeout
                        _ = await _confirmedEvent
                            .WaitAsync(TimeSpan.FromSeconds(ConfirmationCheckIntervalSec), _cts.Token)
                            .ConfigureAwait(false);

                        // check again if there are unconfirmed operations
                        unconfirmedOperations = await _account
                            .LocalStorage
                            .GetUnconfirmedTransactionsAsync<TezosOperation>(
                                currency: _account.Currency)
                            .ConfigureAwait(false);
                    }

                    // get all operation groups from the queue
                    operationCompletionEvents = await GetOperationGroupsFromQueueAsync(_operationsQueue, _cts.Token)
                        .ConfigureAwait(false);

                    if (!operationCompletionEvents.Any())
                    {
                        // no operations -> stop batcher
                        _onComplete?.Invoke(_address);
                        break;
                    }

                    var operationsParameters = new List<TezosOperationParameters>();

                    foreach (var og in operationCompletionEvents)
                        operationsParameters.AddRange(og.OperationsParameters);

                    var from = operationsParameters.First().From;

                    var walletAddress = await _account
                        .GetAddressAsync(from, _cts.Token)
                        .ConfigureAwait(false);

                    var tezosConfig = _account.Config;

                    var publicKey = _account.Wallet
                        .GetPublicKey(tezosConfig, walletAddress.KeyPath, walletAddress.KeyType);

                    var rpcSettings = tezosConfig.GetRpcSettings();
                    var rpc = new TezosRpc(rpcSettings);

                    // fill operation
                    var (operationRequest, fillingError) = await rpc
                        .FillOperationAsync(
                            operationsRequests: operationsParameters,
                            publicKey: publicKey,
                            settings: tezosConfig.GetFillOperationSettings(),
                            cancellationToken: _cts.Token)
                        .ConfigureAwait(false);

                    if (fillingError != null)
                    {
                        _logger?.LogError($"Operation filling error: {fillingError.Value.Message}");

                        // notify all subscribers about error
                        foreach (var op in operationCompletionEvents)
                            op.CompleteWithResult(TezosOperationRequestResult.FromError(operationRequest, fillingError.Value));

                        continue;
                    }

                    _logger?.LogDebug("Operation successfully filled");

                    var (operationId, sendingError) = await _account
                        .SendOperationAsync(operationRequest, _cts.Token)
                        .ConfigureAwait(false);

                    if (sendingError != null)
                    {
                        _logger?.LogError($"Operation sending error: {sendingError.Value.Message}");

                        // notify all subscribers about error
                        foreach (var op in operationCompletionEvents)
                            op.CompleteWithResult(TezosOperationRequestResult.FromError(operationRequest, sendingError.Value));

                        continue;
                    }

                    _logger?.LogDebug($"Operation successfully sent with id {operationId}");

                    // notify all subscribers
                    foreach (var op in operationCompletionEvents)
                        op.CompleteWithResult(TezosOperationRequestResult.FromOperation(operationRequest, operationId));
                }
                catch (OperationCanceledException)
                {
                    // nothing to do
                }
                catch (Exception ex)
                {
                    _logger?.LogError($"Operation batching error: {ex.Message}");

                    var error = new Error(Errors.OperationBatchingError, ex.Message);

                    // notify all subscribers about error
                    if (operationCompletionEvents != null)
                        foreach (var op in operationCompletionEvents)
                            op.CompleteWithResult(TezosOperationRequestResult.FromError(request: null, error));
                }
            }

            _logger?.LogDebug("Batcher for {@addr} completed");
        }

        private static async Task<List<TezosOperationCompletionEvent>> GetOperationGroupsFromQueueAsync(
            AsyncQueue<TezosOperationCompletionEvent> queue,
            CancellationToken cancellationToken)
        {
            var operationGroups = new List<TezosOperationCompletionEvent>();

            if (queue.Count == 0)
                return operationGroups;

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

    public class TezosOperationBatcher : IDisposable
    {
        private readonly ConcurrentDictionary<string, Lazy<TezosAddressOperationBatcher>> _batchers;
        private bool _disposedValue;
        private readonly ILogger _logger;

        public TezosOperationBatcher(ILogger logger = null)
        {
            _batchers = new ConcurrentDictionary<string, Lazy<TezosAddressOperationBatcher>>();
            _logger = logger;
        }

        public async Task<Result<TezosOperationRequestResult>> SendOperationsAsync(
            TezosAccount account,
            IEnumerable<TezosOperationParameters> operationsParameters,
            CancellationToken cancellationToken = default)
        {
            var from = operationsParameters.First().From;

            var lazyBatcher = _batchers.GetOrAdd(
                key: from,
                valueFactory: a => new Lazy<TezosAddressOperationBatcher>(
                    () => new TezosAddressOperationBatcher(
                        address: from,
                        account: account,
                        onComplete: (address) =>
                        {
                            if (!_batchers.TryRemove(address, out var _))
                            {
                                _logger?.LogWarning("Can't find batcher for address {@addr}", address);
                            }

                            _logger?.LogDebug("Batcher for {@addr} removed from batchers list");
                        },
                        logger: _logger)));

            var batcher = lazyBatcher.Value;

            var sendOperationTask = batcher
                .SendOperationAsync(
                    operationsParameters: operationsParameters,
                    cancellationToken: cancellationToken);

            if (!batcher.IsRunning)
                batcher.Start();

            return await sendOperationTask
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