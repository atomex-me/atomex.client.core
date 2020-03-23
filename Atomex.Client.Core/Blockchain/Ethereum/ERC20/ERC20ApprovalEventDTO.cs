using System.Numerics;
using Nethereum.ABI.FunctionEncoding.Attributes;

namespace Atomex.Blockchain.Ethereum.ERC20
{
    [Event("Approval")]
    public class ERC20ApprovalEventDTO : IEventDTO
    {
        [Parameter("address", "owner", 1, true)]
        public string Owner { get; set; }

        [Parameter("address", "spender", 2, true)]
        public string Spender { get; set; }

        [Parameter("uint256", "value", 3, false)]
        public BigInteger Value { get; set; }
    }
}