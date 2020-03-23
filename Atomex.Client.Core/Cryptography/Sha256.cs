using System;
using System.Security.Cryptography;

namespace Atomex.Cryptography
{
    public class Sha256
    {
        public static byte[] Compute(byte[] input, int offset, int count)
        {
            using var sha256 = SHA256.Create();
            return sha256.ComputeHash(input, offset, count);
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