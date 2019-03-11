using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Atomix.Common.Abstract;
using Serilog;

namespace Atomix.Common
{
    public delegate void OnTaskDelegate(BackgroundTask task);

    public abstract class BackgroundTask
    {
        public static TimeSpan DefaultInterval = TimeSpan.FromSeconds(20);

        public OnTaskDelegate CompleteHandler { get; set; }
        public OnTaskDelegate CancelHandler { get; set; }
        public OnTaskDelegate ErrorHandler { get; set; }

        public TimeSpan Interval { get; set; } = DefaultInterval;
        public DateTime LastTryTime { get; set; }

        public abstract Task<bool> CheckCompletion();
    }

    public class BackgroundTaskPerformer : IBackgroundTaskPerformer, IDisposable
    {
        private bool _disposed;
        private Task _worker;
        private CancellationTokenSource _workerCts;
        private readonly ConcurrentQueue<BackgroundTask> _tasks = new ConcurrentQueue<BackgroundTask>();

        public TimeSpan TaskCheckInterval {get; set;} = TimeSpan.FromMilliseconds(100);

        public void Start()
        {
            _workerCts = new CancellationTokenSource();

            _worker = Task.Factory.StartNew(
                function: RunAsync,
                cancellationToken: _workerCts.Token,
                creationOptions: TaskCreationOptions.LongRunning,
                scheduler: TaskScheduler.Default);
        }

        public void Stop()
        {
            if (_workerCts != null && !_workerCts.IsCancellationRequested)
                _workerCts.Cancel();
        }

        public void EnqueueTask(BackgroundTask task)
        {
            _tasks.Enqueue(task);
        }

        private async Task RunAsync()
        {
            Log.Debug("Background task performer successfully started");

            while (!_workerCts.IsCancellationRequested)
            {
                await Task.Delay(TaskCheckInterval)
                    .ConfigureAwait(false);

                if (_tasks.IsEmpty)
                    continue;

                if (!_tasks.TryDequeue(out var task))
                    continue;

                if (task.LastTryTime + task.Interval >= DateTime.Now) {
                    _tasks.Enqueue(task);
                    continue;
                }

                try
                {
                    var completed = await task
                        .CheckCompletion() // todo: not safety, if CheckCompletion method hangs
                        .ConfigureAwait(false);

                    if (completed)
                        continue;
                }
                catch (Exception e)
                {
                    Log.Error(e, "Check completion error");
                }

                task.LastTryTime = DateTime.Now;
                _tasks.Enqueue(task);
            }

            Log.Debug("Background task performer successfully stopped");
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed)
                return;

            if (disposing)
            {
                _workerCts?.Cancel();
                _worker?.Wait();

                _workerCts?.Dispose();
                _worker?.Dispose();
            }

            _disposed = true;
        }

        ~BackgroundTaskPerformer()
        {
            Dispose(false);
        }
    }
}