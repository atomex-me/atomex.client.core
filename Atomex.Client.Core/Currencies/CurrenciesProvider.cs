using System;
using System.Collections.Generic;
using System.Linq;

using Microsoft.Extensions.Configuration;

using Newtonsoft.Json.Linq;

using Atomex.Abstract;
using Atomex.Common.Configuration;
using Atomex.Core;

namespace Atomex
{
    public class CurrenciesProvider : ICurrenciesProvider
    {
        public event EventHandler Updated;

        private readonly IDictionary<Network, ICurrencies> _currencies
            = new Dictionary<Network, ICurrencies>();

        private static readonly Network[] Networks =
        {
            Network.MainNet,
            Network.TestNet
        };
        
        private enum ConfigKey
        {
            Abstract,
            BasedOn,
            Name
        }
        
        public CurrenciesProvider(string configuration)
        {
            var nestedJsonConfig = CreateNestedConfig(configuration);
            
            var buildedConfig = new ConfigurationBuilder()
                .AddJsonString(nestedJsonConfig)
                .Build();

            SetupConfiguration(buildedConfig);
        }

        private void SetupConfiguration(IConfiguration configuration)
        {
            foreach (var network in Networks)
            {
                var networkConfiguration = configuration.GetSection(network.ToString());

                if (!networkConfiguration.GetChildren().Any())
                    continue;

                _currencies.Add(network, new Currencies(networkConfiguration));
            }
        }

        public void Update(IConfiguration configuration)
        {
            foreach (var network in Networks)
            {
                var networkConfiguration = configuration.GetSection(network.ToString());

                if (!networkConfiguration.GetChildren().Any())
                    continue;

                if (networkConfiguration != null && _currencies.TryGetValue(network, out var currencies))
                    currencies.Update(networkConfiguration); 
            }

            Updated?.Invoke(this, EventArgs.Empty);
        }

        public ICurrencies GetCurrencies(Network network)
        {
            return _currencies[network];
        }
        
        public string CreateNestedConfig(string content)
        {
            JObject jObjConfig = JObject.Parse(content);
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

            return jObjConfig.ToString();
        }
    }
}