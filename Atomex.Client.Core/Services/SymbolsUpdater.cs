using System;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Configuration;
using Serilog;

using Atomex.Abstract;
using Atomex.Common;
using Atomex.Common.Configuration;
using Atomex.Services.Abstract;

namespace Atomex.Services
{
    public class SymbolsUpdater : ISymbolsUpdater, IDisposable
    {
        private const string BaseUri = "https://atomex.me/";
        private const string SymbolsConfig = "symbols.json";

        private readonly ISymbolsProvider _symbolsProvider;
        private Task _updaterTask;
        private CancellationTokenSource _cts;

        public TimeSpan UpdateInterval { get; set; } = TimeSpan.FromMinutes(5);
        public bool IsRunning => _updaterTask != null &&
                                !_updaterTask.IsCompleted &&
                                !_updaterTask.IsCanceled &&
                                !_updaterTask.IsFaulted;

        public SymbolsUpdater(ISymbolsProvider symbolsProvider)
        {
            _symbolsProvider = symbolsProvider ?? throw new ArgumentNullException(nameof(symbolsProvider));
        }

        public void Start()
        {
            if (IsRunning)
            {
                Log.Warning("Symbols update task already running");
                return;
            }

            _cts = new CancellationTokenSource();
            _updaterTask = Task.Run(UpdateLoop, _cts.Token);
        }

        public void Stop()
        {
            if (IsRunning)
            {
                _cts.Cancel();
            }
            else
            {
                Log.Warning("Symbols update task already finished");
            }
        }

        private async Task UpdateLoop()
        {
            Log.Information("Run symbols update loop");

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
                    Log.Debug("Symbols update task canceled");
                }
            }

            Log.Information("Background update task finished");
        }

        public async Task UpdateAsync(CancellationToken cancellationToken)
        {
            try
            {
                var response = await HttpHelper.GetAsync(
                        baseUri: BaseUri,
                        relativeUri: SymbolsConfig,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                    return;

                var content = await response
                    .Content
                    .ReadAsStringAsync()
                    .ConfigureAwait(false);

                if (content != null)
                {
                    var configuration = new ConfigurationBuilder()
                        .AddJsonString(content)
                        .Build();

                    _symbolsProvider.Update(configuration);
                }
            }
            catch (Exception e)
            {
                Log.Error(e, "Symbols update error");
            }
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