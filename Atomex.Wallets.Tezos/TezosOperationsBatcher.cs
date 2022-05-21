using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;
using Netezos.Forging.Models;

using Atomex.Blockchain.Tezos;
using Atomex.Common;
using Atomex.Wallets.Tezos.Common;

namespace Atomex.Wallets.Tezos
{
    public class TezosAddressOperationsBatcher : IDisposable
    {
        private const int ConfirmationCheckIntervalSec = 5;

        private readonly string _address;
        private readonly TezosAccount _account;
        private readonly AsyncQueue<TezosOperationCreationRequest> _requestsQueue;
        private CancellationTokenSource _cts;
        private Task _worker;
        
        private readonly ManualResetEventAsync _confirmedEvent;
        private bool _disposedValue;

        public bool IsRunning => _worker != null &&
            !_worker.IsCompleted &&
            !_worker.IsCanceled &&
            !_worker.IsFaulted;

        public TezosAddressOperationsBatcher(
            string address,
            TezosAccount account)
        {
            _address = address ?? throw new ArgumentNullException(nameof(address));
            _account = account ?? throw new ArgumentNullException(nameof(account));
            _requestsQueue = new AsyncQueue<TezosOperationCreationRequest>();
            _confirmedEvent = new ManualResetEventAsync(isSet: false);
        }

        public async Task<(TezosOperation tx, Error error)> SendOperationAsync(
            OperationContent content,
            string from,
            Fee fee,
            GasLimit gasLimit,
            StorageLimit storageLimit,
            Counter counter,
            bool isFinal = true,
            CancellationToken cancellationToken = default)
        {
            var request = new TezosOperationCreationRequest
            {
                Content      = content,
                From         = from,
                Fee          = fee,
                GasLimit     = gasLimit,
                StorageLimit = storageLimit,
                Counter      = counter,
                IsFinal      = isFinal,

                CompletionEvent = new ManualResetEventAsync(isSet: false)
            };

            _requestsQueue.Add(request);

            // wait for completion
            await request
                .CompletionEvent
                .WaitAsync(cancellationToken)
                .ConfigureAwait(false);

            return (
                tx: request.Operation,
                error: request.Error
            );
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
            while (!_cts.IsCancellationRequested)
            {
                //operationRequests.Clear();

                try
                {
                    // get all operation requests from the queue
                    var operationRequests = await GetOperationsFromQueueAsync(_requestsQueue, _cts.Token)
                        .ConfigureAwait(false);

                    // has user defined counters
                    var hasUserDefinedCounters = operationRequests
                        .Any(or => !or.Counter.UseNetwork);

                    // check if there are unconfirmed operations
                    var unconfirmedOperations = await _account
                        .GetUnconfirmedTransactionsAsync<TezosOperation>(cancellationToken: _cts.Token)
                        .ConfigureAwait(false);

                    unconfirmedOperations.Max(o => o.)

                    //while (hasUnconfirmedOps)
                    //{
                    //    // wait for external confirmation event or ConfirmationCheckIntervalSec timeout
                    //    _ = await _confirmedEvent
                    //        .WaitAsync(TimeSpan.FromSeconds(ConfirmationCheckIntervalSec), _cts.Token)
                    //        .ConfigureAwait(false);

                    //    // check again if there are unconfirmed operations
                    //    hasUnconfirmedOps = await HasUnconfirmedOperations(_cts.Token)
                    //        .ConfigureAwait(false);
                    //}



                    // fill operation
                    var (operation, fillingError) = await TezosOperationFiller
                        .FillOperationAsync(
                            operationsRequests: operationRequests,
                            account: _account,
                            cancellationToken: _cts.Token)
                        .ConfigureAwait(false);

                    // sign the operation
                    var error = await _account
                        .SignAsync(operation, _cts.Token)
                        .ConfigureAwait(false);

                    if (error != null)
                    {
                        // notify all subscribers about error
                        foreach (var op in operationRequests)
                            op.CompleteWithError(error);

                        continue; // continue waiting new operations
                    }

                    // broadcast the operation
                    var currencyConfig = _account.Configuration;

                    var api = new TezosApi(
                        settings: currencyConfig.ApiSettings,
                        logger: _account.Logger);

                    var (txId, broadcastError) = await api
                        .BroadcastAsync(operation, _cts.Token)
                        .ConfigureAwait(false);

                    if (broadcastError != null)
                    {
                        // notify all subscribers about error
                        foreach (var op in operationRequests)
                            op.CompleteWithError(broadcastError);

                        continue; // continue waiting new operations
                    }

                    // save operation in local db
                    var upsertResult = _account.DataRepository
                        .UpsertTransactionAsync(operation, _cts.Token)
                        .ConfigureAwait(false);

                    // notify all subscribers
                    foreach (var op in operationRequests)
                        op.Complete(operation);
                }
                catch (OperationCanceledException)
                {
                    // nothing to do
                }
                catch (Exception ex)
                {
                    _account.Logger.LogError($"Operation batching error: {ex.Message}");

                    var error = new Error(Errors.OperationBatchingError, ex.Message);

                    // notify all subscribers about error
                    foreach (var op in operationRequests)
                        op.CompleteWithError(error);
                }
            }
        }

        public static async Task<IEnumerable<TezosOperationCreationRequest>> GetOperationsFromQueueAsync(
            AsyncQueue<TezosOperationCreationRequest> queue,
            CancellationToken cancellationToken)
        {
            var operationsRequests = new List<TezosOperationCreationRequest>();

            bool isFinal;

            do
            {
                var op = await queue
                    .TakeAsync(cancellationToken)
                    .ConfigureAwait(false);

                isFinal = op.IsFinal;

                operationsRequests.Add(op);

            } while (!isFinal || queue.Count > 0);

            return operationsRequests;
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposedValue)
            {
                if (disposing)
                {
                    Stop();

                    _requestsQueue?.Clear();
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

    public class TezosOperationsBatcher : IDisposable
    {
        private readonly ConcurrentDictionary<string, Lazy<TezosAddressOperationsBatcher>> _batchers;
        private bool _disposedValue;

        public async Task<(TezosOperation tx, Error error)> SendOperationAsync(
            TezosAccount account,
            OperationContent content,
            string from,
            Fee fee,
            GasLimit gasLimit,
            StorageLimit storageLimit,
            Counter counter,
            bool isFinal = true,
            CancellationToken cancellationToken = default)
        {
            var lazyBatcher = _batchers.GetOrAdd(
                key: from,
                valueFactory: a => new Lazy<TezosAddressOperationsBatcher>(
                    () => new TezosAddressOperationsBatcher(from, account)));

            var batcher = lazyBatcher.Value;

            if (!batcher.IsRunning)
                batcher.Start();

            return await batcher
                .SendOperationAsync(
                    content: content,
                    from: from,
                    fee: fee,
                    gasLimit: gasLimit,
                    storageLimit: storageLimit,
                    counter: counter,
                    isFinal: isFinal,
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