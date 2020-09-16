using System.IO;
using System.Security;
using System.Security.Cryptography;

using Atomex.Common;

namespace Atomex.Cryptography
{
    public static class Aes
    {
        private const int Aes256KeySize = 256;
        private const int Aes256BlockSize = 128;
        private const int SaltSize = 16;
        private const int Rfc2898Iterations = 52768;

        public static byte[] Encrypt(
            byte[] plainBytes,
            byte[] salt,
            byte[] key,
            byte[] iv,
            int keySize = Aes256KeySize)
        {
            using var aes = new AesManaged
            {
                KeySize = keySize,
                Key = key,
                IV = iv,
                Mode = CipherMode.CBC
            };

            using var ms = new MemoryStream();
            ms.Write(buffer: salt, offset: 0, count: salt.Length);

            using (var cs = new CryptoStream(ms, aes.CreateEncryptor(), CryptoStreamMode.Write))
                cs.Write(buffer: plainBytes, offset: 0, count: plainBytes.Length);

            return ms.ToArray();
        }

        public static byte[] Encrypt(
            byte[] plainBytes,
            byte[] keyBytes,
            int keySize = Aes256KeySize,
            int saltSize = SaltSize,
            int iterations = Rfc2898Iterations)
        {
            var salt = Rand.SecureRandomBytes(length: saltSize);

            using var key = new Rfc2898DeriveBytes(keyBytes, salt, iterations);

            return Encrypt(
                plainBytes: plainBytes,
                salt: salt,
                key: key.GetBytes(cb: keySize / 8),
                iv: key.GetBytes(cb: Aes256BlockSize / 8),
                keySize: keySize);
        }

        public static byte[] Encrypt(
            byte[] plainBytes,
            SecureString password,
            int keySize = Aes256KeySize,
            int saltSize = SaltSize,
            int iterations = Rfc2898Iterations)
        {
            var passwordBytes = password.ToBytes();

            return Encrypt(plainBytes, passwordBytes, keySize, saltSize, iterations);
        }

        public static byte[] Decrypt(
            byte[] encryptedBytes,
            byte[] key,
            byte[] iv,
            int keySize = Aes256KeySize,
            int saltSize = SaltSize)
        {
            using var aes = new AesManaged
            {
                KeySize = keySize,
                Key = key,
                IV = iv,
                Mode = CipherMode.CBC
            };

            using var ms = new MemoryStream();
            using (var cs = new CryptoStream(ms, aes.CreateDecryptor(), CryptoStreamMode.Write))
                cs.Write(
                    buffer: encryptedBytes,
                    offset: saltSize,
                    count: encryptedBytes.Length - saltSize);

            return ms.ToArray();
        }

        public static byte[] Decrypt(
            byte[] encryptedBytes,
            byte[] keyBytes,
            int keySize = Aes256KeySize,
            int saltSize = SaltSize,
            int iterations = Rfc2898Iterations)
        {
            var salt = encryptedBytes.Copy(offset: 0, count: saltSize);

            using var key = new Rfc2898DeriveBytes(keyBytes, salt, iterations);

            return Decrypt(
                encryptedBytes: encryptedBytes,
                key: key.GetBytes(cb: keySize / 8),
                iv: key.GetBytes(cb: Aes256BlockSize / 8),
                keySize: keySize,
                saltSize: saltSize);
        }

        public static byte[] Decrypt(
            byte[] encryptedBytes,
            SecureString password,
            int keySize = Aes256KeySize,
            int saltSize = SaltSize,
            int iterations = Rfc2898Iterations)
        {
            var passwordBytes = password.ToBytes();

            return Decrypt(encryptedBytes, passwordBytes, keySize, saltSize, iterations);
        }
    }
}