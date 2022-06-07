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
        private const string ApiBaseUrl = "https://api.baking-bad.org/";

        public static async Task<IEnumerable<BakerData>> GetBakers(Network network,
            CancellationToken cancellationToken = default)
        {
            if (network == Network.TestNet)
                return new List<BakerData>();

            using var response = await HttpHelper.GetAsync(
                    baseUri: ApiBaseUrl,
                    relativeUri: "v2/bakers",
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            var result = ParseBakersToViewModel(
                JsonConvert.DeserializeObject<List<Baker>>(response.Content
                    .ReadAsStringAsync()
                    .WaitForResult()));

            if (result != null)
                return result;

            Log.Error("Error while trying to fetch bakers list");
            return new List<BakerData>();

        }

        public static async Task<BakerData> GetBaker(string address, Network network,
            CancellationToken cancellationToken = default)
        {
            if (network == Network.TestNet)
                return new BakerData();

            using var response = await HttpHelper.GetAsync(
                    baseUri: ApiBaseUrl,
                    relativeUri: $"v2/bakers/{address}",
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            return response.StatusCode == HttpStatusCode.OK
                ? ParseBakerToViewModel(
                    JsonConvert.DeserializeObject<Baker>(
                        response.Content
                        .ReadAsStringAsync()
                        .WaitForResult()))
                : new BakerData();
        }

        private static IEnumerable<BakerData> ParseBakersToViewModel(List<Baker> bakers)
        {
            var bakersList = bakers
                .Where(baker => baker.payoutAccuracy != "suspicious" &&
                                baker.payoutTiming != "suspicious" &&
                                baker.serviceHealth == "active" &&
                                baker.serviceType != "exchange" &&
                                baker.openForDelegation)
                .OrderBy(baker => baker.IsFull)
                .ThenByDescending(baker => baker.insuranceCoverage)
                .ThenByDescending(baker => baker, new BakerComparer())
                .ThenByDescending(baker => baker.estimatedRoi)
                .Select(baker => new BakerData
                {
                    Address = baker.address,
                    Logo = baker.logo,
                    Name = baker.name,
                    Fee = baker.fee,
                    MinDelegation = baker.minDelegation,
                    StakingAvailable = Math.Round(baker.freeSpace, 6),
                    EstimatedRoi = baker.estimatedRoi
                });
                
            return bakersList;
        }

        private static BakerData ParseBakerToViewModel(Baker baker)
        {
            var result = new BakerData
            {
                Address = baker.address,
                Logo = baker.logo,
                Name = baker.name,
                Fee = baker.fee,
                MinDelegation = baker.minDelegation,
                StakingAvailable = Math.Round(baker.freeSpace, 6),
                EstimatedRoi = baker.estimatedRoi
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

    public class BakerComparer : IComparer<BbApi.Baker>
    {
        private static string[] PayoutAccuracyPriority => new[] { "precise", "no_data", "inaccurate", "suspicious" };
        private static string[] PayoutTimingPriority => new[] { "stable", "no_data", "unstable", "suspicious" };

        public int Compare(BbApi.Baker y, BbApi.Baker x)
        {
            var accuracyRes = Array.IndexOf(PayoutAccuracyPriority, x?.payoutAccuracy) -
                              Array.IndexOf(PayoutAccuracyPriority, y?.payoutAccuracy);
            var payoutTimingRes = Array.IndexOf(PayoutTimingPriority, x?.payoutTiming) -
                                  Array.IndexOf(PayoutTimingPriority, y?.payoutTiming);
            return accuracyRes + payoutTimingRes;
        }
    }
}