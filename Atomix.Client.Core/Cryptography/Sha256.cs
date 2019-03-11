using System;
using System.Security.Cryptography;
using System.Text;

namespace Atomix.Cryptography
{
    public class Sha256 : Hash
    {
        public override byte[] ComputeHash(byte[] input, int offset, int count)
        {
            return SHA256.Create().ComputeHash(input, offset, count);
        }

        public static byte[] Compute(byte[] input, int offset, int count)
        {
            return new Sha256().ComputeHash(input, offset, count);
        }

        public static byte[] Compute(byte[] input)
        {
            return new Sha256().ComputeHash(input, 0, input.Length);
        }

        public static byte[] Compute(string input, Encoding encoding)
        {
            return new Sha256().ComputeHash(input, encoding);
        }

        public static byte[] Compute(byte[] input, int iterations)
        {
            if (iterations <= 0)
                throw new ArgumentException("Iterations count must be greater than zero", nameof(iterations));

            var result = input;

            for (var i = 0; i < iterations; ++i)
                result = Compute(result);

            return result;
        }
    }
}