using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using Atomex.Common;
using Atomex.Core;

namespace Atomex.Blockchain.BitcoinBased
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
                    baseUri: "https://bitcoinfees.earn.com/api/v1/",
                    requestUri: "fees/recommended",
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