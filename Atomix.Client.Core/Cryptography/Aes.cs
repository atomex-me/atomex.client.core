using System;
using System.IO;
using System.Security;
using System.Security.Cryptography;
using Atomix.Common;

namespace Atomix.Cryptography
{
    public class Aes
    {
        private const int Aes256KeySize = 256;
        private const int SaltSize = 16;
        private const int Iterations = 52768;

        public static byte[] Encrypt(byte[] plainBytes, byte[] keyBytes)
        {
            byte[] encryptedBytes;

            var salt = Rand.SecureRandomBytes(SaltSize);

            using (var aes = new AesManaged())
            {
                var key = new Rfc2898DeriveBytes(keyBytes, salt, Iterations);

                aes.KeySize = Aes256KeySize;
                aes.Key = key.GetBytes(aes.KeySize / 8);
                aes.IV = key.GetBytes(aes.BlockSize / 8);
                aes.Mode = CipherMode.CBC;

                using (var ms = new MemoryStream())
                {
                    ms.Write(salt, 0, salt.Length);

                    using (var cs = new CryptoStream(ms, aes.CreateEncryptor(), CryptoStreamMode.Write))
                        cs.Write(plainBytes, 0, plainBytes.Length);

                    encryptedBytes = ms.ToArray();
                }
            }


            return encryptedBytes;
        }

        public static byte[] Encrypt(byte[] plainBytes, SecureString password)
        {
            var passwordBytes = password.ToBytes();
            var result = Encrypt(plainBytes, passwordBytes);

            Array.Clear(passwordBytes, 0, passwordBytes.Length);

            return result;
        }

        public static byte[] Decrypt(byte[] encryptedBytes, byte[] keyBytes)
        {
            byte[] decryptedBytes;

            var salt = encryptedBytes.Copy(0, SaltSize);

            using (var aes = new AesManaged())
            {
                var key = new Rfc2898DeriveBytes(keyBytes, salt, Iterations);

                aes.KeySize = Aes256KeySize;
                aes.Key = key.GetBytes(aes.KeySize / 8);
                aes.IV = key.GetBytes(aes.BlockSize / 8);
                aes.Mode = CipherMode.CBC;

                using (var ms = new MemoryStream())
                {
                    using (var cs = new CryptoStream(ms, aes.CreateDecryptor(), CryptoStreamMode.Write))
                        cs.Write(encryptedBytes, SaltSize, encryptedBytes.Length - SaltSize);

                    decryptedBytes = ms.ToArray();
                }
            }

            return decryptedBytes;
        }

        public static byte[] Decrypt(byte[] encryptedBytes, SecureString password)
        {
            var passwordBytes = password.ToBytes();
            var result = Decrypt(encryptedBytes, passwordBytes);

            Array.Clear(passwordBytes, 0, passwordBytes.Length);

            return result;
        }
    }
}