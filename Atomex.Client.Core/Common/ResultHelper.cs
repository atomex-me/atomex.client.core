using System;
using System.Threading;
using System.Threading.Tasks;

namespace Atomex.Common
{
    public static class ResultHelper
    {
        public static async Task<Result<T>> TryDo<T>(
            Func<CancellationToken, Task<Result<T>>> func,
            int attempts = 10,
            int attemptsIntervalMs = 1000,
            CancellationToken cancellationToken = default)
        {
            var attempt = 0;

            while (attempt < attempts)
            {
                ++attempt;

                var funcResult = await func(cancellationToken)
                    .ConfigureAwait(false);

                if (funcResult == null || funcResult.IsConnectionError)
                {
                    await Task.Delay(attemptsIntervalMs, cancellationToken)
                        .ConfigureAwait(false);
                }
                else return funcResult;
            }

            return null;
        }
    }
}