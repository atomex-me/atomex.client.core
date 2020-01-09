using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Atomex.Blockchain.Tezos.Internal;
using Atomex.Common;
using Atomex.Core;
using Newtonsoft.Json;
using Serilog;

namespace Atomex.Blockchain.Tezos
{
    public class BbApi
    {
        private readonly string _rpcNodeUri;
        private readonly string _apiBaseUrl;

        public BbApi(Atomex.Tezos currency)
        {
            _rpcNodeUri = currency.RpcNodeUri;
            _apiBaseUrl = "https://api.baking-bad.org/";
        }
        
        public async Task<IEnumerable<BakerData>> GetBakers(Network network, CancellationToken cancellationToken = default)
        {
            if (network == Network.TestNet)
                return new List<BakerData>();
            
            var rpc = new Rpc(_rpcNodeUri);

            var level = (await rpc.GetHeader())["level"].ToObject<int>();
            var currentCycle = (level - 1) / 4096;
            
            var result = await HttpHelper.GetAsync(
                    baseUri: _apiBaseUrl,
                    requestUri: "v1/bakers?insurance=true&configs=true&rating=true",
                    responseHandler: response => ParseBakersToViewModel(JsonConvert.DeserializeObject<List<Baker>>(response.Content.ReadAsStringAsync().WaitForResult()), currentCycle),
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (result == null)
            {
                Log.Error("Error while trying to fetch bakers list");
                return new List<BakerData>();
            }
                
            return result;
        }
        
        public async Task<BakerData> GetBaker(string address, Network network, CancellationToken cancellationToken = default)
        {
            if (network == Network.TestNet)
                return new BakerData();
            
            var rpc = new Rpc(_rpcNodeUri);

            var level = (await rpc.GetHeader())["level"].ToObject<int>();
            var currentCycle = (level - 1) / 4096;
            
            return await HttpHelper.GetAsync(
                    baseUri: _apiBaseUrl,
                    requestUri: $"v1/bakers/{address}?insurance=true&configs=true&rating=true",
                    responseHandler: response => ParseBakerToViewModel(JsonConvert.DeserializeObject<Baker>(response.Content.ReadAsStringAsync().WaitForResult()), currentCycle),
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }
        
        private IEnumerable<BakerData> ParseBakersToViewModel(List<Baker> bakers, int currentCycle)
        {
            var result = bakers
                .Where(x => x.rating.status != 2 && x.rating.status != 6)
                .OrderByDescending(x => (x.insurance?.coverage ?? 0))
                .ThenByDescending(y => y.rating.actualRoi)
                .Select(x => new BakerData
                {
                    Address = x.address,
                    Logo = $"{_apiBaseUrl}/logos/{x.logo}",
                    Name = x.name,
                    Fee = x.config.fee.FirstOrDefault(y => y.cycle <= currentCycle)?.value ?? 0,
                    StakingAvailable = x.stakingCapacity - x.stakingBalance
                });

            return result;
        }
        
        private BakerData ParseBakerToViewModel(Baker baker, int currentCycle)
        {
            var result = new BakerData
            {
                Address = baker.address,
                Logo = $"{_apiBaseUrl}/logos/{baker.logo}",
                Name = baker.name,
                Fee = baker.config.fee.FirstOrDefault(y => y.cycle <= currentCycle)?.value ?? 0,
                StakingAvailable = baker.stakingCapacity - baker.stakingBalance
            };

            return result;
        }
        
        public class Baker
        {
            public string address { get; set; }
            public string name { get; set; }
            public string logo { get; set; }
            public string site { get; set; }
            public decimal balance { get; set; }
            public decimal stakingBalance { get; set; }
            public decimal stakingCapacity { get; set; }
            public decimal estimatedRoi { get; set; }
            public Config config { get; set; }
            public Rating rating { get; set; }
            public Insurance insurance { get; set; }

        }

        public class Config
        {
            public string address { get; set; }
            public List<ConfigValue<decimal>> fee { get; set; }
            public List<ConfigValue<decimal>> minBalance { get; set; }
            public List<ConfigValue<bool>> payoutFee { get; set; }
            public List<ConfigValue<int>> payoutDelay { get; set; }
            public List<ConfigValue<int>> payoutPeriod { get; set; }
            public List<ConfigValue<decimal>> minPayout { get; set; }
            public List<ConfigValue<int>> rewardStruct { get; set; }
            public List<ConfigValue<decimal>> payoutRatio { get; set; }
            public List<string> ignored { get; set; }
            public List<string> sources { get; set; }
        }
        public class Rating
        {
            public string address { get; set; }
            public string delegator { get; set; }
            public string sharedConfig { get; set; }
            public int fromCycle { get; set; }
            public int toCycle { get; set; }
            public decimal avgRolls { get; set; }
            public decimal actualRoi { get; set; }
            public decimal prevRoi { get; set; }
            public int status { get; set; }
        }
        public class Insurance
        {
            public string address { get; set; }
            public string insuranceAddress { get; set; }
            public decimal insuranceAmount { get; set; }
            public decimal coverage { get; set; }
        }

        public class ConfigValue<T>
        {
            public int cycle { get; set; }
            public T value { get; set; }
        }
    }
}