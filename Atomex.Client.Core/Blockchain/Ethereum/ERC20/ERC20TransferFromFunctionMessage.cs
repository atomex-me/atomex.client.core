using System.Numerics;
using Nethereum.ABI.FunctionEncoding.Attributes;
using Nethereum.Contracts;

namespace Atomex.Blockchain.Ethereum.ERC20
{
    [Function("transferFrom")]
    public class ERC20TransferFromFunctionMessage : FunctionMessage
    {
        [Parameter("address", "_from", 1)]
        public string From { get; set; }

        [Parameter("address", "_to", 2)]
        public string To { get; set; }

        [Parameter("uint256", "_value", 3)]
        public BigInteger Value { get; set; }
    }
}