using System.Security;
using System.Threading.Tasks;

using Atomex.Common;

namespace Atomex.Cryptography
{
    public static class Argon2id
    {
        public static Task<byte[]> ComputeAsync(
            SecureString password,
            byte[] salt,
            int degreeOfParallelism = 8,
            int iterations = 6,
            int memorySize = 64 * 1024,
            int hashLength = 64)
        {
            var passwordBytes = password.ToBytes();

            using var argon2id = new Konscious.Security.Cryptography.Argon2id(passwordBytes)
            {
                Salt = salt,
                DegreeOfParallelism = degreeOfParallelism,
                Iterations = iterations,
                MemorySize = memorySize
            };

            return argon2id.GetBytesAsync(hashLength);
        }

        public static byte[] Compute(
            SecureString password,
            byte[] salt,
            int degreeOfParallelism = 8,  // 4 cores
            int iterations = 6,
            int memorySize = 64 * 1024,   // 64 Mb  
            int hashLength = 64)
        {
            var passwordBytes = password.ToBytes();

            using var argon2id = new Konscious.Security.Cryptography.Argon2id(passwordBytes)
            {
                Salt = salt,
                DegreeOfParallelism = degreeOfParallelism,
                Iterations = iterations,
                MemorySize = memorySize
            };

            return argon2id.GetBytes(hashLength);
        }
    }
}