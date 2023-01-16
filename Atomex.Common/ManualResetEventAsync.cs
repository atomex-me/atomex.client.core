using System;
using System.Threading;
using System.Threading.Tasks;

namespace Atomex.Wallets.Tezos.Common
{
    public class ManualResetEventAsync
    {
        private const int WaitIndefinitly = -1;

        private readonly bool _runSynchronousContinuationsOnSetThread = false;

        private volatile TaskCompletionSource<bool> _completionSource = new();

        public ManualResetEventAsync(bool isSet)
            : this(isSet: isSet, runSynchronousContinuationsOnSetThread: false)
        {
        }

        public ManualResetEventAsync(bool isSet, bool runSynchronousContinuationsOnSetThread)
        {
            _runSynchronousContinuationsOnSetThread = runSynchronousContinuationsOnSetThread;

            if (isSet)
                _completionSource.TrySetResult(true);
        }

        public Task WaitAsync() =>
            AwaitCompletion(WaitIndefinitly, default);

        public Task WaitAsync(CancellationToken token) =>
            AwaitCompletion(WaitIndefinitly, token);

        public Task<bool> WaitAsync(TimeSpan timeout, CancellationToken token) =>
            AwaitCompletion((int)timeout.TotalMilliseconds, token);

        public Task<bool> WaitAsync(TimeSpan timeout) =>
            AwaitCompletion((int)timeout.TotalMilliseconds, default);

        public void Set()
        {
            if (_runSynchronousContinuationsOnSetThread)
            {
                _completionSource.TrySetResult(true);
            }
            else
            {
                Task.Run(() => _completionSource.TrySetResult(true));
            }
        }

        public void Reset()
        {
            var currentCompletionSource = _completionSource;

            if (!currentCompletionSource.Task.IsCompleted)
                return;

            Interlocked.CompareExchange(ref _completionSource, new TaskCompletionSource<bool>(), currentCompletionSource);
        }

        private async Task<bool> AwaitCompletion(int timeoutMS, CancellationToken token)
        {
            if (timeoutMS < -1 || timeoutMS > int.MaxValue)
                throw new ArgumentException("The timeout must be either -1ms (indefinitely) or a positive ms value <= int.MaxValue");

            CancellationTokenSource timeoutToken = null;

            if (!token.CanBeCanceled)
            {
                if (timeoutMS == -1)
                    return await _completionSource.Task;

                timeoutToken = new CancellationTokenSource();
            }
            else
            {
                timeoutToken = CancellationTokenSource.CreateLinkedTokenSource(token);
            }

            using (timeoutToken)
            {
                var delayTask = Task.Delay(timeoutMS, timeoutToken.Token)
                    .ContinueWith((result) => { var e = result.Exception; }, TaskContinuationOptions.ExecuteSynchronously);

                var resultingTask = await Task.WhenAny(_completionSource.Task, delayTask)
                    .ConfigureAwait(false);

                if (resultingTask != delayTask)
                {
                    timeoutToken.Cancel();
                    return true;
                }

                token.ThrowIfCancellationRequested();
                return false;
            }
        }
    }
}