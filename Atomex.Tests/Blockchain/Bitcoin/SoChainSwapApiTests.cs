using NBitcoin;

using Atomex.Blockchain.Abstract;
using Atomex.Blockchain.Bitcoin.SoChain;

namespace Atomex.Blockchain.Bitcoin
{
    public class SoChainSwapApiTests : BitcoinSwapApiTests
    {
        public override IBlockchainSwapApi CreateApi(string currency, string network)
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