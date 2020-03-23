using Org.BouncyCastle.Crypto.Digests;

namespace Atomex.Cryptography
{
    public class HmacBlake2b
    {
        public static byte[] Compute(byte[] input, int offset, int count, int digestSize)
        {
            var blake2b = new Blake2bDigest(digestSize);

            var result = new byte[blake2b.GetDigestSize()];
            blake2b.BlockUpdate(input, offset, count);
            blake2b.DoFinal(result, 0);

            return result;
        }

        public static byte[] Compute(byte[] input, int digestSize) =>
            Compute(input, 0, input.Length, digestSize);
    }
}