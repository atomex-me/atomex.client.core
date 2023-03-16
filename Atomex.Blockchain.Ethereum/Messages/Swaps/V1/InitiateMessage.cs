using System.Numerics;

using Nethereum.ABI.FunctionEncoding.Attributes;
using Nethereum.Contracts;

namespace Atomex.Blockchain.Ethereum.Messages.Swaps.V1
{
    [Function("initiate")]
    public class InitiateMessage : FunctionMessage
    {
        [Parameter("bytes32", "_hashedSecret", 1)]
        public byte[] HashedSecret { get; set; }

        [Parameter("address", "_participant", 2)]
        public string Participant { get; set; }

        [Parameter("uint", "_refundTimestamp", 3)]
        public long RefundTimestamp { get; set; }

        [Parameter("uint256", "_payoff", 4)]
        public BigInteger RedeemFee { get; set; }
    }
}