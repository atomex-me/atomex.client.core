using System.Numerics;

using Nethereum.ABI.FunctionEncoding.Attributes;

namespace Atomex.Blockchain.Ethereum.Dto.Erc20.Swaps.V1
{
    [Event("Initiated")]
    public class Erc20InitiatedEventDto
    {
        [Parameter("bytes32", "_hashedSecret", 1, true)]
        public byte[] HashedSecret { get; set; }

        [Parameter("address", "_contract", 2, true)]
        public string ERC20Contract { get; set; }

        [Parameter("address", "_participant", 3, true)]
        public string Participant { get; set; }

        [Parameter("address", "_initiator", 4, false)]
        public string Initiator { get; set; }

        [Parameter("uint256", "_refundTimestamp", 5, false)]
        public BigInteger RefundTimestamp { get; set; }

        [Parameter("uint256", "_countdown", 6, false)]
        public BigInteger Countdown { get; set; }

        [Parameter("uint256", "_value", 7, false)]
        public BigInteger Value { get; set; }

        [Parameter("uint256", "_payoff", 8, false)]
        public BigInteger RedeemFee { get; set; }

        [Parameter("bool", "_active", 9, false)]
        public bool Active { get; set; }
    }
}