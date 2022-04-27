using Atomex.Common.Memory;
using Atomex.Cryptography;
using Atomex.Wallets.Keys;

namespace Atomex.Wallets
{
    public class Bip32Ed25519ExtKeyTests : KeyTests<Bip32Ed25519ExtKey>
    {
        public override int KeySize => 32;

        public override int SignatureSize => 64;

        public override Bip32Ed25519ExtKey CreateKey(int keySize, out byte[] seed)
        {
            seed = Rand.SecureRandomBytes(keySize);

            using var secureSeed = new SecureBytes(seed);

            return new Bip32Ed25519ExtKey(secureSeed);
        }
    }
}