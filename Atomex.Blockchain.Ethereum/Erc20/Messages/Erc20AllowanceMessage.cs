using Nethereum.ABI.FunctionEncoding.Attributes;
using Nethereum.Contracts;

namespace Atomex.Blockchain.Ethereum.Erc20.Messages
{
    [Function("allowance", "uint")]
    public class Erc20AllowanceMessage : FunctionMessage
    {
        [Parameter("address", "_owner", 1)]
        public string Owner { get; set; }

        [Parameter("address", "_spender", 2)]
        public string Spender { get; set; }
    }
}