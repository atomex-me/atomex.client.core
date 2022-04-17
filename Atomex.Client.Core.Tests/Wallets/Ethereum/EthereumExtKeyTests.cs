using Atomex.Common.Memory;
using Atomex.Cryptography;

namespace Atomex.Wallets.Ethereum
{
    public class EthereumExtKeyTests : KeyTests<EthereumExtKey>
    {
        public override int KeySize => 32;
        public override int SignatureSize => 71;

        public override EthereumExtKey CreateKey(int keySize, out byte[] seed)
        {
            seed = Rand.SecureRandomBytes(keySize);

            using var secureSeed = new SecureBytes(seed);

            return new EthereumExtKey(secureSeed);
        }
    }
}