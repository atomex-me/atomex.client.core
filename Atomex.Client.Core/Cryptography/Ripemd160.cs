using System.Text;
using Org.BouncyCastle.Crypto.Digests;

namespace Atomex.Cryptography
{
    public class Ripemd160 : Hash
    {
        public override byte[] ComputeHash(byte[] input, int offset, int count)
        {
            var ripemd160 = new RipeMD160Digest();
            var result = new byte[ripemd160.GetDigestSize()];

            ripemd160.BlockUpdate(input, offset, count);
            ripemd160.DoFinal(result, 0);

            return result;
        }

        public static byte[] Compute(byte[] input, int offset, int count)
        {
            return new Ripemd160().ComputeHash(input, offset, count);
        }

        public static byte[] Compute(byte[] input)
        {
            return new Ripemd160().ComputeHash(input, 0, input.Length);
        }

        public static byte[] Compute(string input, Encoding encoding)
        {
            return new Ripemd160().ComputeHash(input, encoding);
        }
    }
}