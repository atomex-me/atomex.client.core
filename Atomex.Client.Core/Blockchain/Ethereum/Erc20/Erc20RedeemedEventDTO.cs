﻿using Nethereum.ABI.FunctionEncoding.Attributes;

namespace Atomex.Blockchain.Ethereum.Erc20
{
    [Event("Redeemed")]
    public class Erc20RedeemedEventDTO : IEventDTO
    {
        [Parameter("bytes32", "_hashedSecret", 1, true)]
        public byte[] HashedSecret { get; set; }

        [Parameter("bytes32", "_secret", 2, false)]
        public byte[] Secret { get; set; }
    }
}