using System;
using System.Security.Cryptography;

namespace Atomex.Cryptography
{
    public class Sha512
    {
        public static byte[] Compute(byte[] input, int offset, int count)
        {
            using var sha512 = SHA512.Create();
            return sha512.ComputeHash(input, offset, count);
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