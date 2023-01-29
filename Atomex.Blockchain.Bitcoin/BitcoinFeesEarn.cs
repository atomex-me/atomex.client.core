using System.Threading;
using System.Threading.Tasks;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using Atomex.Common;

namespace Atomex.Blockchain.Bitcoin
{
    public class FeeRate
    {
        public long FastestFee { get; set; }
        public long HalfHourFee { get; set; }
        public long HourFee { get; set; }
    }

    public static class BitcoinFeesEarn
    {
        public const string BaseUrl = "https://services.atomex.me/";

        public static async Task<Result<FeeRate>> GetFeeRateAsync(CancellationToken cancellationToken)
        {
            using var response = await HttpHelper
                .GetAsync(
                    baseUri: BaseUrl,
                    relativeUri: "getbitcoinfees",
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            var content = await response
                .Content
                .ReadAsStringAsync()
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
                return new Error((int)response.StatusCode, "Error status code received");

            var feeRates = JsonConvert.DeserializeObject<JObject>(content);

            return feeRates != null
                ? new Result<FeeRate>
                {
                    Value = new FeeRate()
                    {
                        FastestFee  = feeRates.Value<long>("fastestFee"),
                        HalfHourFee = feeRates.Value<long>("halfHourFee"),
                        HourFee     = feeRates.Value<long>("hourFee")
                    }
                }
                : new Result<FeeRate>
                {
                    Error = new Error(Errors.InvalidResponse, "Invalid response")
                };
        }
    }
}