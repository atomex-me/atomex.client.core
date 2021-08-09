using System;
using System.Threading;
using System.Threading.Tasks;

namespace Atomex.Common
{
    public static class AsyncHelpers
    {
        public static T WaitForResult<T>(this Task<T> task)
        {
            try
            {
                return RunSync(() => task);
            }
            catch (Exception)
            {
                if (task.Exception != null)
                    throw task.Exception;

                throw;
            }
        }
        public static void WaitForResult(this Task task)
        {
            try
            {
                RunSync(() => task);
            }
            catch (Exception)
            {
                if (task.Exception != null)
                    throw task.Exception;

                throw;
            }
        }

        private static readonly TaskFactory _myTaskFactory =
            new (CancellationToken.None,
                 TaskCreationOptions.None,
                 TaskContinuationOptions.None,
                 TaskScheduler.Default);

        public static TResult RunSync<TResult>(Func<Task<TResult>> func)
        {
            return _myTaskFactory
              .StartNew(func)
              .Unwrap()
              .GetAwaiter()
              .GetResult();
        }

        public static void RunSync(Func<Task> func)
        {
            _myTaskFactory
              .StartNew(func)
              .Unwrap()
              .GetAwaiter()
              .GetResult();
        }
    }
}