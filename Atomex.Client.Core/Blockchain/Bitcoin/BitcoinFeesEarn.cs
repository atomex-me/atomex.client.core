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
        public static async Task<Result<FeeRate>> GetFeeRateAsync(CancellationToken cancellationToken)
        {
            return await HttpHelper.GetAsyncResult(
                    baseUri: "https://services.atomex.me/",
                    requestUri: "getbitcoinfees",
                    responseHandler: (response, content) =>
                    {
                        var feeRates = JsonConvert.DeserializeObject<JObject>(content);

                        return feeRates != null
                            ? new Result<FeeRate>(new FeeRate()
                            {
                                FastestFee  = feeRates.Value<long>("fastestFee"),
                                HalfHourFee = feeRates.Value<long>("halfHourFee"),
                                HourFee     = feeRates.Value<long>("hourFee")
                            })
                            : new Result<FeeRate>(new Error(Errors.InvalidResponse, "Invalid response"));
                    },
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }
    }
}