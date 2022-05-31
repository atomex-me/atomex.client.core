using NBitcoin;

using Atomex.Blockchain.Bitcoin.SoChain;
using Atomex.Blockchain.Bitcoin.Abstract;

namespace Atomex.Blockchain.Bitcoin
{
    public class SoChainApiTests : BitcoinApiTests
    {
        public override IBitcoinApi CreateApi(string currency, string network)
        {
            return new SoChainApi(
                currency: currency,
                settings: new SoChainSettings
                {
                    BaseUri = SoChainApi.Uri,
                    Network = network
                });
        }

        public override Network ResolveNetwork(string currency, string network) =>
            SoChainApi.ResolveNetwork(currency, network);
    }
}