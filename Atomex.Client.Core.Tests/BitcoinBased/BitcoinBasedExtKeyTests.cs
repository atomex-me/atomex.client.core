using Atomex.Client.Core.Tests.Wallets;
using Atomex.Common.Memory;
using Atomex.Cryptography;
using Atomex.Wallets.BitcoinBased;

namespace Atomex.Client.Core.Tests.BitcoinBased
{
    public class BitcoinBasedExtKeyTests : ExtKeyTests<BitcoinBasedExtKey>
    {
        public override int KeySize => 32;
        public override int SignatureSize => 71;

        public override BitcoinBasedExtKey CreateKey(int keySize, out byte[] seed)
        {
            seed = Rand.SecureRandomBytes(keySize);

            using var secureSeed = new SecureBytes(seed);

            return new BitcoinBasedExtKey(secureSeed);
        }
    }
}