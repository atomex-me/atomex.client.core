using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;

using Atomex.MarketData.Entities;

namespace Atomex.MarketData.Abstract
{
    public abstract class QuotesProvider : IQuotesProvider, IDisposable
    {
        public event EventHandler QuotesUpdated;
        public event EventHandler AvailabilityChanged;

        public const string Usd = "USD";
        protected ILogger? Log;
        private Task _updaterTask;
        private CancellationTokenSource _cts;
        protected Dictionary<string, Quote> Quotes { get; set; }

        public DateTime LastUpdateTime { get; protected set; }
        public DateTime LastSuccessUpdateTime { get; protected set; }
        public virtual bool IsAvailable { get; protected set; }
        public TimeSpan UpdateInterval { get; set; } = TimeSpan.FromMinutes(1);
        public bool IsRunning => _updaterTask is { IsCompleted: false, IsCanceled: false, IsFaulted: false };

        public QuotesProvider(ILogger? log = null)
        {
            Log = log;
        }

        public virtual void Start()
        {
            if (IsRunning)
            {
                Log?.LogWarning("Background update task already running");
                return;
            }

            _cts = new CancellationTokenSource();

            _updaterTask = Task.Run(UpdateLoop, _cts.Token);
        }

        private async Task UpdateLoop()
        {
            Log?.LogInformation("Run background update loop");

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
                    Log?.LogDebug("Background update task canceled");
                }
            }

            Log?.LogInformation("Background update task finished");
        }

        public virtual void Stop()
        {
            if (IsRunning)
            {
                _cts.Cancel();
            }
            else
            {
                Log?.LogWarning("Background update task already finished");
            }
        }

        public abstract Quote? GetQuote(string currency, string baseCurrency);
        public abstract Quote? GetQuote(string symbol);
        public abstract Task UpdateAsync(CancellationToken cancellation = default);

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