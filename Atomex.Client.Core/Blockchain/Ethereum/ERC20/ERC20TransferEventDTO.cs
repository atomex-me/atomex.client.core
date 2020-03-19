using System.Numerics;
using Nethereum.ABI.FunctionEncoding.Attributes;

namespace Atomex.Blockchain.Ethereum.ERC20
{
    [Event("Transfer")]
    public class ERC20TransferEventDTO : IEventDTO
    {
        [Parameter("address", "from", 1, true)]
        public string From { get; set; }

        [Parameter("address", "to", 2, true)]
        public string To { get; set; }

        [Parameter("uint256", "value", 3, false)]
        public BigInteger Value { get; set; }
    }
}