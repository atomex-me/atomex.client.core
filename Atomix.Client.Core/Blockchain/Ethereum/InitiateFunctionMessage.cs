using Nethereum.ABI.FunctionEncoding.Attributes;
using Nethereum.Contracts;

namespace Atomix.Blockchain.Ethereum
{
    [Function("initiate")]
    public class InitiateFunctionMessage : FunctionMessage
    {
        [Parameter("bytes32", "_hashedSecret", 1)]
        public byte[] HashedSecret { get; set; }

        [Parameter("uint", "_refundTime", 2)]
        public uint RefundTime { get; set; }

        [Parameter("address", "_participant", 3)]
        public string Participant { get; set; }

        [Parameter("bool", "_master", 4)]
        public bool Master { get; set; }
    }
}