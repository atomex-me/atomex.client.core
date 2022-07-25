using System;
using System.Security;

using Atomex.Cryptography.Abstract;

namespace Atomex.Common
{
    public static class SessionPasswordHelper
    {
        private const int DefaultHashIterationsCount = 10;

        public static string GetSessionPassword(
            SecureString password,
            int hashIterationsCount = DefaultHashIterationsCount)
        {
            var passwordBytes = password.ToBytes();
            var passwordHash = HashAlgorithm.Sha256.Hash(passwordBytes, hashIterationsCount);

            Array.Clear(passwordBytes, 0, passwordBytes.Length);

            return Convert.ToBase64String(passwordHash);
        }

        public static byte[] GetSessionPasswordBytes(
            SecureString password,
            int hashIterationsCount = DefaultHashIterationsCount)
        {
            var passwordBytes = password.ToBytes();
            var passwordHash = HashAlgorithm.Sha256.Hash(passwordBytes, hashIterationsCount);

            Array.Clear(passwordBytes, 0, passwordBytes.Length);

            return passwordHash;
        }
    }
}