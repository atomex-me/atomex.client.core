using System;
using System.Threading.Tasks;
using Atomex.Abstract;
using Atomex.Common;
using Atomex.Common.Configuration;
using Atomex.Subsystems.Abstract;
using Microsoft.Extensions.Configuration;
using Serilog;

namespace Atomex.Subsystems
{
    public class CurrenciesUpdater : ICurrenciesUpdater
    {
        private const string BaseUri = "https://atomex.me/";
        private const string CurrenciesConfig = "currencies.json";

        private readonly ICurrenciesProvider _currenciesProvider;

        public CurrenciesUpdater(ICurrenciesProvider currenciesProvider)
        {
            _currenciesProvider = currenciesProvider ?? throw new ArgumentNullException(nameof(currenciesProvider));
        }

        public async Task UpdateAsync()
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
                        })
                    .ConfigureAwait(false);

                if (content != null)
                {
                    var configuration = new ConfigurationBuilder()
                        .AddJsonString(content, "currencies.json")
                        .Build();
                    
                    _currenciesProvider.Update(configuration);
                }
            }
            catch (Exception e)
            {
                Log.Error(e, "Currencies update error");
            }
        }
    }
}