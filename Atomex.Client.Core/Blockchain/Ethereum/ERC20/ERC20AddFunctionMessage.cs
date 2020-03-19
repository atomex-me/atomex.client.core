using Nethereum.ABI.FunctionEncoding.Attributes;
using System.Numerics;
using Nethereum.Contracts;

namespace Atomex.Blockchain.Ethereum.ERC20
{
    [Function("add")]
    public class ERC20AddFunctionMessage : FunctionMessage
    {
        [Parameter("bytes32", "_hashedSecret", 1)]
        public byte[] HashedSecret { get; set; }

        [Parameter("uint256", "_value", 2)]
        public BigInteger Value { get; set; }
    }
}
