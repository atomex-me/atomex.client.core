﻿using Atomex.Common.Memory;
using Atomex.Cryptography;
using Atomex.Wallets.Tezos;

using Atomex.Client.Core.Tests.Wallets;

namespace Atomex.Client.Core.Tests.Tezos
{
    public class TezosKeyTests : KeyTests<TezosKey>
    {
        public override int KeySize => 32;

        public override int SignatureSize => 64;

        public override TezosKey CreateKey(int keySize, out byte[] seed)
        {
            seed = Rand.SecureRandomBytes(keySize);

            using var secureSeed = new SecureBytes(seed);

            return new TezosKey(secureSeed);
        }
    }
}