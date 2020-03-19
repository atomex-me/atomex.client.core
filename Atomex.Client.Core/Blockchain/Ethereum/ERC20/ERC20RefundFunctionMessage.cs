using Nethereum.ABI.FunctionEncoding.Attributes;
using Nethereum.Contracts;

namespace Atomex.Blockchain.Ethereum.ERC20
{
    [Function("refund")]
    public class ERC20RefundFunctionMessage : FunctionMessage
    {
        [Parameter("bytes32", "_hashedSecret", 1)]
        public byte[] HashedSecret { get; set; }
    }
}
