using Atomex.Blockchain.Ethereum.EtherScan;
using Atomex.Wallet.Abstract;

namespace Atomex.Wallet.Ethereum
{
    public class EthereumMaxAmountEstimation : MaxAmountEstimation
    {
        public GasPrice GasPrice { get; set; }
    }
}