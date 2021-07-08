﻿using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;

using Microsoft.Extensions.Configuration;
using Serilog;

using Atomex.Abstract;
using Atomex.Common;
using Atomex.Common.Configuration;
using Atomex.Subsystems.Abstract;
using Newtonsoft.Json.Linq;

namespace Atomex.Subsystems
{
    public class CurrenciesUpdater : ICurrenciesUpdater, IDisposable
    {
        // private const string BaseUri = "https://atomex.me/";
        // private const string CurrenciesConfig = "coins.v2.json";

        // todo: reupload to Atomex domain
        private const string BaseUri = "https://pi.turborouter.keenetic.pro";
        private const string CurrenciesConfig = "seafilef/2b0875c0769f44ab97d0/?dl=1";
        
        private enum ConfigKey
        {
            Abstract,
            BasedOn,
            Name
        }

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
                    JObject jObjConfig = JObject.Parse(content);
                    CreateNestedConfig(jObjConfig);

                    var configuration = new ConfigurationBuilder()
                        .AddJsonString(jObjConfig.ToString())
                        .Build();

                    _currenciesProvider.Update(configuration);
                }
            }
            catch (Exception e)
            {
                Log.Error(e, "Currencies update error");
            }
        }

        private void CreateNestedConfig(JObject jObjConfig)
        {
            IList<string> abstractCurrencies;
            IList<string> networkKeys = jObjConfig.Properties().Select(p => p.Name).ToList();

            foreach (var networkKey in networkKeys)
            {
                abstractCurrencies = new List<string>();
                var network = jObjConfig[networkKey] as JObject;
                
                // filling abstract currencies
                foreach (JObject currency in network.Values())
                {
                    if (currency.Value<bool>(nameof(ConfigKey.Abstract)))
                    {
                        abstractCurrencies.Add(currency.Value<string>(nameof(ConfigKey.Name)));
                    
                        var basedOnCurr = network[currency.Value<string>(nameof(ConfigKey.BasedOn))] as JObject;
                        var clonedBasedOnCurr = (JObject) basedOnCurr.DeepClone();
                    
                        clonedBasedOnCurr.Merge(currency, new JsonMergeSettings
                        {
                            MergeArrayHandling = MergeArrayHandling.Union
                        });

                        var stringed = clonedBasedOnCurr.ToString();
                        network[currency.Value<string>(nameof(ConfigKey.Name))] = clonedBasedOnCurr;
                    }
                }

                // filling tokens
                foreach (JObject currency in network.Values())
                {
                    if (currency.Value<bool>(nameof(ConfigKey.Abstract)) == true || 
                        currency.Value<string>(nameof(ConfigKey.BasedOn)) == null)
                    {
                        continue;
                    }
                
                    var basedOnCurr = network[currency.Value<string>(nameof(ConfigKey.BasedOn))] as JObject;
                    var clonedBasedOnCurr = (JObject) basedOnCurr.DeepClone();
                
                    clonedBasedOnCurr.Merge(currency, new JsonMergeSettings
                    {
                        MergeArrayHandling = MergeArrayHandling.Union
                    });
                
                    network[currency.Value<string>(nameof(ConfigKey.Name))] = clonedBasedOnCurr;
                }

                foreach (var abstractCurr in abstractCurrencies)
                {
                    network.Property(abstractCurr).Remove();
                }
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