using System;
using System.Collections.Generic;
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

        public static void RunSync(Func<Task> task)
        {
            var oldContext = SynchronizationContext.Current;
            var synch = new ExclusiveSynchronizationContext();
            SynchronizationContext.SetSynchronizationContext(synch);
            synch.Post(async _ =>
            {
                try
                {
                    await task();
                }
                catch (Exception e)
                {
                    synch.InnerException = e;
                    throw;
                }
                finally
                {
                    synch.EndMessageLoop();
                }

            }, null);

            synch.BeginMessageLoop();

            SynchronizationContext.SetSynchronizationContext(oldContext);
        }

        public static T RunSync<T>(Func<Task<T>> task)
        {
            var ret = default(T);

            var oldContext = SynchronizationContext.Current;
            var syncContext = new ExclusiveSynchronizationContext();

            SynchronizationContext.SetSynchronizationContext(syncContext);

            syncContext.Post(async _ =>
            {
                try
                {
                    ret = await task();
                }
                catch (Exception e)
                {
                    syncContext.InnerException = e;
                    throw;
                }
                finally
                {
                    syncContext.EndMessageLoop();
                }

            }, null);

            syncContext.BeginMessageLoop();

            SynchronizationContext.SetSynchronizationContext(oldContext);

            return ret;
        }

        private class ExclusiveSynchronizationContext : SynchronizationContext
        {
            private bool _done;
            private readonly AutoResetEvent _workItemsWaiting = new(false);
            private readonly Queue<Tuple<SendOrPostCallback, object>> _items = new();

            public Exception InnerException { get; set; }

            public override void Send(SendOrPostCallback d, object state)
            {
                throw new NotSupportedException("We cannot send to our same thread");
            }

            public override void Post(SendOrPostCallback d, object state)
            {
                lock (_items)
                    _items.Enqueue(Tuple.Create(d, state));  

                _workItemsWaiting.Set();
            }

            public void EndMessageLoop()
            {
                Post(_ => _done = true, null);
            }

            public void BeginMessageLoop()
            {
                while (!_done)
                {
                    Tuple<SendOrPostCallback, object> task = null;

                    lock (_items)
                        if (_items.Count > 0)
                            task = _items.Dequeue();

                    if (task != null)
                    {
                        task.Item1(task.Item2);

                        if (InnerException != null) // the method threw an exception
                            throw new AggregateException("AsyncHelpers.Run method threw an exception.", InnerException);
                    }
                    else
                    {
                        _workItemsWaiting.WaitOne();
                    }
                }
            }

            public override SynchronizationContext CreateCopy()
            {
                return this;
            }
        }
    }
}