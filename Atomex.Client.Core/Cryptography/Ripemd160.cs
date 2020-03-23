using System;
using Org.BouncyCastle.Crypto.Digests;

namespace Atomex.Cryptography
{
    public class Ripemd160
    {
        public static byte[] Compute(byte[] input, int offset, int count)
        {
            var ripemd160 = new RipeMD160Digest();
            var result = new byte[ripemd160.GetDigestSize()];

            ripemd160.BlockUpdate(input, offset, count);
            ripemd160.DoFinal(result, 0);

            return result;
        }

        public static byte[] Compute(byte[] input) =>
            Compute(input, 0, input.Length);

        public static byte[] Compute(byte[] input, int offset, int count, int iterations)
        {
            if (iterations <= 0)
                throw new ArgumentException("Iterations count must be greater than zero", nameof(iterations));

            var result = Compute(input, offset, count);

            for (var i = 0; i < iterations - 1; ++i)
                result = Compute(result);

            return result;
        }

        public static byte[] Compute(byte[] input, int iterations) =>
            Compute(input, 0, input.Length, iterations);
    }
}