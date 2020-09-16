using Atomex.Common.Memory;
using Atomex.Cryptography;
using Atomex.Wallets.Ethereum;

using Atomex.Client.Core.Tests.Wallets;

namespace Atomex.Client.Core.Tests.Ethereum
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