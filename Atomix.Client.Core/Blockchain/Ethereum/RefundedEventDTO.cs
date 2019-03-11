using Nethereum.ABI.FunctionEncoding.Attributes;

namespace Atomix.Blockchain.Ethereum
{
    [Event("Refunded")]
    public class RefundedEventDTO : IEventDTO
    {
        [Parameter("bytes32", "_hashedSecret", 1, true)]
        public byte[] HashedSecret { get; set; }

        [Parameter("uint", "_refundTime", 2, false)]
        public uint RefundTime { get; set; }
    }
}