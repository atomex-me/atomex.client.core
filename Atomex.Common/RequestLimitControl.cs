using System;
using System.Threading;
using System.Threading.Tasks;

namespace Atomex.Common
{
    public class RequestLimitControl : IDisposable
    {
        private readonly SemaphoreSlim _semaphoreSlim = new(1);
        private readonly int _delayMs;
        private long _lastTimeStampMs;
        private bool _disposed;

        public RequestLimitControl(int delayMs)
        {
            _delayMs = delayMs;
        }

        public async Task WaitAsync(CancellationToken cancellationToken)
        {
            var isCompleted = false;

            while (!isCompleted)
            {
                if (cancellationToken.IsCancellationRequested)
                    cancellationToken.ThrowIfCancellationRequested();

                await _semaphoreSlim
                    .WaitAsync(cancellationToken)
                    .ConfigureAwait(false);

                var timeStampMs = DateTimeOffset.Now.ToUnixTimeMilliseconds();
                var differenceMs = timeStampMs - _lastTimeStampMs;

                if (differenceMs < _delayMs)
                {
                    _semaphoreSlim.Release();

                    await Task.Delay((int)(_delayMs - differenceMs), cancellationToken)
                        .ConfigureAwait(false);
                }
                else
                {
                    _lastTimeStampMs = timeStampMs;

                    _semaphoreSlim.Release();

                    isCompleted = true;
                }
            }
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    _semaphoreSlim.Dispose();
                }

                _disposed = true;
            }
        }

        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}