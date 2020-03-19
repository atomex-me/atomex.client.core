using Nethereum.ABI.FunctionEncoding.Attributes;

namespace Atomex.Blockchain.Ethereum.ERC20
{
    [Event("Refunded")]
    public class ERC20RefundedEventDTO : IEventDTO
    {
        [Parameter("bytes32", "_hashedSecret", 1, true)]
        public byte[] HashedSecret { get; set; }
    }
}
