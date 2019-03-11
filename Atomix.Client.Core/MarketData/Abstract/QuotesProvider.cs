using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Serilog;

namespace Atomix.MarketData.Abstract
{
    public abstract class QuotesProvider : ICurrencyQuotesProvider, IDisposable
    {
        private Task _updaterTask;
        private CancellationTokenSource _cts;
        protected Dictionary<string, Quote> Quotes { get; set; }

        public event EventHandler QuotesUpdated;
        public event EventHandler AvailabilityChanged;

        public DateTime LastUpdateTime { get; protected set; }
        public DateTime LastSuccessUpdateTime { get; protected set; }
        public bool IsAvailable { get; protected set; }

        public TimeSpan UpdateInterval { get; set; } = TimeSpan.FromMinutes(1);
        public bool IsRunning => _updaterTask != null &&
                                !_updaterTask.IsCompleted &&
                                !_updaterTask.IsCanceled &&
                                !_updaterTask.IsFaulted;

        public void Start()
        {
            if (IsRunning) {
                Log.Warning("Background update task already running");
                return;
            }

            _cts = new CancellationTokenSource();

            _updaterTask = Task.Run(UpdateLoop, _cts.Token);
        }

        private async Task UpdateLoop()
        {
            Log.Information("Run background update loop");

            while (!_cts.IsCancellationRequested)
            {
                try
                {
                    await UpdateAsync(_cts.Token)
                        .ConfigureAwait(false);

                    await Task.Delay(UpdateInterval, _cts.Token)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    Log.Debug("Background update task canceled");
                }
            }

            Log.Information("Background update task finished");
        }

        public void Stop()
        {
            if (IsRunning)
            {
                _cts.Cancel();
            }
            else
            {
                Log.Warning("Background update task already finished");
            }
        }

        public abstract Quote GetQuote(string currency, string baseCurrency);

        protected abstract Task UpdateAsync(CancellationToken cancellation = default(CancellationToken));

        protected void RiseQuotesUpdatedEvent(EventArgs args)
        {
            QuotesUpdated?.Invoke(this, args);
        }

        protected void RiseAvailabilityChangedEvent(EventArgs args)
        {
            AvailabilityChanged?.Invoke(this, args);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (IsRunning && _cts != null)
                {
                    _cts.Cancel();
                    _updaterTask?.Wait();
                }

                _updaterTask?.Dispose();
                _cts?.Dispose();
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
    }
}