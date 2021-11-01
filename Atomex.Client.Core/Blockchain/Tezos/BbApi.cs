using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

using Newtonsoft.Json;
using Serilog;

using Atomex.Common;
using Atomex.Core;

namespace Atomex.Blockchain.Tezos
{
    public static class BbApi
    {
        private static readonly string _apiBaseUrl = "https://api.baking-bad.org/";
        
        public static async Task<IEnumerable<BakerData>> GetBakers(Network network, CancellationToken cancellationToken = default)
        {
            if (network == Network.TestNet)
                return new List<BakerData>();
            
            var result = await HttpHelper.GetAsync(
                    baseUri: _apiBaseUrl,
                    requestUri: "v2/bakers",
                    responseHandler: response => ParseBakersToViewModel(JsonConvert.DeserializeObject<List<Baker>>(response.Content.ReadAsStringAsync().WaitForResult())),
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (result == null)
            {
                Log.Error("Error while trying to fetch bakers list");
                return new List<BakerData>();
            }
                
            return result;
        }
        
        public static async Task<BakerData> GetBaker(string address, Network network, CancellationToken cancellationToken = default)
        {
            if (network == Network.TestNet)
                return new BakerData();
            
            return await HttpHelper.GetAsync(
                    baseUri: _apiBaseUrl,
                    requestUri: $"v2/bakers/{address}",
                    responseHandler: response =>
                        response.StatusCode == HttpStatusCode.OK ?
                            ParseBakerToViewModel(
                                JsonConvert.DeserializeObject<Baker>(
                                    response
                                    .Content
                                    .ReadAsStringAsync()
                                    .WaitForResult())) :
                            new BakerData(),
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }
        
        private static IEnumerable<BakerData> ParseBakersToViewModel(List<Baker> bakers)
        {
            var result = bakers
                .Where(x => x.payoutAccuracy != "suspicious" && x.payoutTiming != "suspicious" 
                         && x.serviceHealth == "active" && x.openForDelegation && x.serviceType != "exchange")
                .OrderBy(x => x.IsFull)
                .ThenByDescending(x => x.insuranceCoverage)
                .ThenByDescending(y => y.estimatedRoi)
                .Select(x => new BakerData
                {
                    Address          = x.address,
                    Logo             = x.logo,
                    Name             = x.name,
                    Fee              = x.fee,
                    MinDelegation    = x.minDelegation,
                    StakingAvailable = Math.Round(x.freeSpace, 6),
                    EstimatedRoi     = x.estimatedRoi
                });

            return result;
        }
        
        private static BakerData ParseBakerToViewModel(Baker baker)
        {
            var result = new BakerData
            {
                Address          = baker.address,
                Logo             = baker.logo,
                Name             = baker.name,
                Fee              = baker.fee,
                MinDelegation    = baker.minDelegation,
                StakingAvailable = Math.Round(baker.freeSpace, 6),
                EstimatedRoi     = baker.estimatedRoi
            };

            return result;
        }
        
        public class Baker
        {
            public string address { get; set; }
            public string name { get; set; }
            public string logo { get; set; }
            public decimal freeSpace { get; set; }
            public decimal fee { get; set; }
            public decimal minDelegation { get; set; }
            public bool openForDelegation { get; set; }
            public decimal estimatedRoi { get; set; }
            public string serviceType { get; set; }
            public string serviceHealth { get; set; }
            public string payoutTiming { get; set; }
            public string payoutAccuracy { get; set; }
            public decimal insuranceCoverage { get; set; }
            public bool IsFull => freeSpace <= 0;
        }
    }
}