﻿using System.Numerics;

using Nethereum.ABI.FunctionEncoding.Attributes;
using Nethereum.Contracts;

namespace Atomex.Blockchain.Ethereum.Erc20
{
    [Function("add")]
    public class Erc20AddFunctionMessage : FunctionMessage
    {
        [Parameter("bytes32", "_hashedSecret", 1)]
        public byte[] HashedSecret { get; set; }

        [Parameter("uint256", "_value", 2)]
        public BigInteger Value { get; set; }
    }
}