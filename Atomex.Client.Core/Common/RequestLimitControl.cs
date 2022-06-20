using System;
using System.Threading;
using System.Threading.Tasks;

namespace Atomex.Common
{
    public class RequestLimitControl
    {
        private readonly object _sync = new object();
        private readonly int _minDelayBetweenRequestMs;
        private long _lastRequestTimeStampMs;

        public RequestLimitControl(int minDelayBetweenRequestMs)
        {
            _minDelayBetweenRequestMs = minDelayBetweenRequestMs;
        }

        public async Task WaitAsync(CancellationToken cancellationToken)
        {
            var isCompleted = false;

            while (!isCompleted)
            {
                if (cancellationToken.IsCancellationRequested)
                    cancellationToken.ThrowIfCancellationRequested();

                Monitor.Enter(_sync);

                var timeStampMs = DateTime.Now.ToUnixTimeMs();
                var differenceMs = timeStampMs - _lastRequestTimeStampMs;

                if (differenceMs < _minDelayBetweenRequestMs)
                {
                    Monitor.Exit(_sync);

                    await Task.Delay((int)(_minDelayBetweenRequestMs - differenceMs), cancellationToken)
                        .ConfigureAwait(false);
                }
                else
                {
                    _lastRequestTimeStampMs = timeStampMs;
                    Monitor.Exit(_sync);

                    isCompleted = true;
                }
            }
        }
    }
}