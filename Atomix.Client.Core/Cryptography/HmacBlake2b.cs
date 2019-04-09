using Org.BouncyCastle.Crypto.Digests;

namespace Atomix.Cryptography
{
    public class HmacBlake2b : Hash
    {
        public int DigestSize { get; }

        public HmacBlake2b(int digestSize)
        {
            DigestSize = digestSize;
        }

        public override byte[] ComputeHash(byte[] input, int offset, int count)
        {
            var blake2b = new Blake2bDigest(DigestSize);
            var result = new byte[blake2b.GetDigestSize()];

            blake2b.BlockUpdate(input, offset, count);
            blake2b.DoFinal(result, 0);

            return result;
        }
    }
}
