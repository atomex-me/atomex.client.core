using System.Numerics;
using Nethereum.ABI.FunctionEncoding.Attributes;

namespace Atomix.Blockchain.Ethereum
{
    [Event("Initiated")]
    public class InitiatedEventDTO : IEventDTO
    {
        [Parameter("bytes32", "_hashedSecret", 1, true)]
        public byte[] HashedSecret { get; set; }

        [Parameter("uint", "_initTimestamp", 2, false)]
        public uint InitTimestamp { get; set; }

        [Parameter("uint", "_refundTime", 3, false)]
        public uint RefundTime { get; set; }

        [Parameter("address", "_participant", 4, true)]
        public string Participant { get; set; }

        [Parameter("address", "_initiator", 5, true)]
        public string Initiator { get; set; }

        [Parameter("uint256", "_value", 6, false)]
        public BigInteger Value { get; set; }
    }
}