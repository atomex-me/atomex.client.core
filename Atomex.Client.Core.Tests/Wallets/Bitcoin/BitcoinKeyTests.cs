using Atomex.Common.Memory;
using Atomex.Cryptography;

namespace Atomex.Wallets.Bitcoin
{
    public class BitcoinKeyTests : KeyTests<BitcoinKey>
    {
        public override int KeySize => 32;
        public override int SignatureSize => 71;

        public override BitcoinKey CreateKey(int keySize, out byte[] seed)
        {
            seed = Rand.SecureRandomBytes(keySize);

            using var secureSeed = new SecureBytes(seed);

            return new BitcoinKey(secureSeed);
        }
    }
}