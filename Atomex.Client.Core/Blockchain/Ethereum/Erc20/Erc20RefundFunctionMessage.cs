using Nethereum.ABI.FunctionEncoding.Attributes;
using Nethereum.Contracts;

namespace Atomex.Blockchain.Ethereum.Erc20
{
    [Function("refund")]
    public class Erc20RefundFunctionMessage : FunctionMessage
    {
        [Parameter("bytes32", "_hashedSecret", 1)]
        public byte[] HashedSecret { get; set; }
    }
}
