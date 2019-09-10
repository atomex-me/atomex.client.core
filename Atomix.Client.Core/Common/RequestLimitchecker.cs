using System;
using System.Threading;
using System.Threading.Tasks;

namespace Atomix.Common
{
    public class RequestLimitChecker
    {
        private readonly object _sync = new object();
        private long _lastRequestTimeStampMs;
        private int _minDelayBetweenRequestMs = 1000;

        public RequestLimitChecker(
            int minDelayBetweenRequestMs)
        {
            _minDelayBetweenRequestMs = minDelayBetweenRequestMs;
        }

        public async Task WaitIfNeeded(
            CancellationToken cancellationToken)
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