﻿using System.Numerics;

using Nethereum.ABI.FunctionEncoding.Attributes;
using Nethereum.Contracts;

namespace Atomex.Blockchain.Ethereum.Erc20.Messages
{
    [Function("approve")]
    public class Erc20ApproveMessage : FunctionMessage
    {
        [Parameter("address", "_spender", 1)]
        public string Spender { get; set; }

        [Parameter("uint256", "_value", 2)]
        public BigInteger Value { get; set; }
    }
}