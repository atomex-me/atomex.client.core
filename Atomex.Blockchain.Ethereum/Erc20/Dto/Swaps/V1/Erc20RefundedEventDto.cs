using Nethereum.ABI.FunctionEncoding.Attributes;

namespace Atomex.Blockchain.Ethereum.Erc20.Dto.Swaps.V1
{
    [Event("Refunded")]
    public class Erc20RefundedEventDTO : IEventDTO
    {
        [Parameter("bytes32", "_hashedSecret", 1, true)]
        public byte[] HashedSecret { get; set; }
    }
}
