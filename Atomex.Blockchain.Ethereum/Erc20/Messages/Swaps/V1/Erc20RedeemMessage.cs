using Nethereum.ABI.FunctionEncoding.Attributes;
using Nethereum.Contracts;

namespace Atomex.Blockchain.Ethereum.Erc20.Messages.Swaps.V1
{
    [Function("redeem")]
    public class Erc20RedeemMessage : FunctionMessage
    {
        [Parameter("bytes32", "_hashedSecret", 1)]
        public byte[] HashedSecret { get; set; }

        [Parameter("bytes32", "_secret", 2)]
        public byte[] Secret { get; set; }
    }
}
