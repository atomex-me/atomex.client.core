using System.IO;
using System.Security;
using System.Security.Cryptography;
using Atomex.Common;

namespace Atomex.Cryptography
{
    public static class Aes
    {
        private const int Aes256KeySize = 256;
        private const int SaltSize = 16;
        private const int Iterations = 52768;

        private static byte[] Encrypt(
            byte[] plainBytes,
            byte[] keyBytes,
            int keySize = Aes256KeySize,
            int saltSize = SaltSize,
            int iterations = Iterations)
        {
            var salt = Rand.SecureRandomBytes(length: saltSize);

            using var aes = new AesManaged();
            using var key = new Rfc2898DeriveBytes(keyBytes, salt, iterations);

            aes.KeySize = keySize;
            aes.Key = key.GetBytes(cb: aes.KeySize / 8);
            aes.IV = key.GetBytes(cb: aes.BlockSize / 8);
            aes.Mode = CipherMode.CBC;

            using var ms = new MemoryStream();
            ms.Write(buffer: salt, offset: 0, count: salt.Length);

            using (var cs = new CryptoStream(ms, aes.CreateEncryptor(), CryptoStreamMode.Write))
                cs.Write(buffer: plainBytes, offset: 0, count: plainBytes.Length);

            return ms.ToArray();
        }

        public static byte[] Encrypt(
            byte[] plainBytes,
            SecureString password,
            int keySize = Aes256KeySize,
            int saltSize = SaltSize,
            int iterations = Iterations)
        {
            using var scopedPasswordBytes = new ScopedBytes(password.ToBytes());

            return Encrypt(plainBytes, scopedPasswordBytes, keySize, saltSize, iterations);
        }

        private static byte[] Decrypt(
            byte[] encryptedBytes,
            byte[] keyBytes,
            int keySize = Aes256KeySize,
            int saltSize = SaltSize,
            int iterations = Iterations)
        {
            var salt = encryptedBytes.Copy(offset: 0, count: saltSize);

            using var aes = new AesManaged();
            using var key = new Rfc2898DeriveBytes(keyBytes, salt, iterations);

            aes.KeySize = keySize;
            aes.Key = key.GetBytes(cb: aes.KeySize / 8);
            aes.IV = key.GetBytes(cb: aes.BlockSize / 8);
            aes.Mode = CipherMode.CBC;

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
            SecureString password,
            int keySize = Aes256KeySize,
            int saltSize = SaltSize,
            int iterations = Iterations)
        {
            using var scopedPasswordBytes = new ScopedBytes(password.ToBytes());

            return Decrypt(encryptedBytes, scopedPasswordBytes, keySize, saltSize, iterations);
        }
    }

}