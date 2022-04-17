using Atomex.Common.Memory;
using Atomex.Cryptography;

namespace Atomex.Wallets.Bitcoin
{
    public class BitcoinExtKeyTests : ExtKeyTests<BitcoinExtKey>
    {
        public override int KeySize => 32;
        public override int SignatureSize => 71;

        public override BitcoinExtKey CreateKey(int keySize, out byte[] seed)
        {
            seed = Rand.SecureRandomBytes(keySize);

            using var secureSeed = new SecureBytes(seed);

            return new BitcoinExtKey(secureSeed);
        }
    }
}