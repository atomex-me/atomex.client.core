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
    public class CurrenciesUpdater : ICurrenciesUpdater, IDisposable
    {
        private const string BaseUri = "https://atomex.me/";
        private const string CurrenciesConfig = "coins.v7.json";

        private readonly ICurrenciesProvider _currenciesProvider;
        private Task _updaterTask;
        private CancellationTokenSource _cts;

        public TimeSpan UpdateInterval { get; set; } = TimeSpan.FromMinutes(5);
        public bool IsRunning => _updaterTask != null &&
                                !_updaterTask.IsCompleted &&
                                !_updaterTask.IsCanceled &&
                                !_updaterTask.IsFaulted;

        public CurrenciesUpdater(ICurrenciesProvider currenciesProvider)
        {
            _currenciesProvider = currenciesProvider ?? throw new ArgumentNullException(nameof(currenciesProvider));
        }

        public void Start()
        {
            if (IsRunning)
            {
                Log.Warning("Currencies update task already running");
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
                Log.Warning("Currencies update task already finished");
            }
        }

        private async Task UpdateLoop()
        {
            Log.Information("Run currencies update loop");

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
                    Log.Debug("Currencies update task canceled");
                }
            }

            Log.Information("Background update task finished");
        }

        public async Task UpdateAsync(CancellationToken cancellationToken)
        {
            try
            {
                var content = await HttpHelper.GetAsync(
                        baseUri: BaseUri,
                        requestUri: CurrenciesConfig,
                        responseHandler: response =>
                        {
                            if (!response.IsSuccessStatusCode)
                                return null;

                            return response.Content
                                .ReadAsStringAsync()
                                .WaitForResult();
                        },
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                if (content != null)
                {
                    var stringConfig = _currenciesProvider.CreateNestedConfig(content);

                    var configuration = new ConfigurationBuilder()
                        .AddJsonString(stringConfig)
                        .Build();

                    _currenciesProvider.Update(configuration);
                }
            }
            catch (Exception e)
            {
                Log.Error(e, "Currencies update error");
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