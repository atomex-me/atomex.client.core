using System.Numerics;
using Nethereum.ABI.FunctionEncoding.Attributes;
using Nethereum.Contracts;

namespace Atomex.Blockchain.Ethereum.ERC20
{
    [Function("allowance", "uint")]
    public class ERC20AllowanceFunctionMessage : FunctionMessage
    {
        [Parameter("address", "_owner", 1)]
        public string Owner { get; set; }

        [Parameter("address", "_spender", 2)]
        public string Spender { get; set; }
    }
}