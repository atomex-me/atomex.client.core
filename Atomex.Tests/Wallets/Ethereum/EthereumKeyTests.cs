﻿using Atomex.Common.Memory;
using Atomex.Cryptography;

namespace Atomex.Wallets.Ethereum
{
    public class EthereumKeyTests : KeyTests<EthereumKey>
    {
        public override int KeySize => 32;
        public override int SignatureSize => 71;

        public override EthereumKey CreateKey(int keySize, out byte[] seed)
        {
            seed = Rand.SecureRandomBytes(keySize);

            using var secureSeed = new SecureBytes(seed);

            return new EthereumKey(secureSeed);
        }
    }
}