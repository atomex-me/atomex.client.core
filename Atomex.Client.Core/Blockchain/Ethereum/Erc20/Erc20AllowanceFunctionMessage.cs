using Nethereum.ABI.FunctionEncoding.Attributes;
using Nethereum.Contracts;

namespace Atomex.Blockchain.Ethereum.Erc20
{
    [Function("allowance", "uint")]
    public class Erc20AllowanceFunctionMessage : FunctionMessage
    {
        [Parameter("address", "_owner", 1)]
        public string Owner { get; set; }

        [Parameter("address", "_spender", 2)]
        public string Spender { get; set; }
    }
}