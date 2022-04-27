using System;
using System.IO;
using System.Security.Cryptography;

using SymmetricAlgorithm = Atomex.Cryptography.Abstract.SymmetricAlgorithm;

namespace Atomex.Cryptography.DotNet
{
    public class AesCbc : SymmetricAlgorithm
    {
        private const int Aes256KeySize = 256;
        private const int SaltSize = 16;
        private const int Iterations = 52768;

        private readonly int _keySize;
        private readonly int _saltSize;
        private readonly int _iterations;

        public AesCbc(
            int keySize = Aes256KeySize,
            int saltSize = SaltSize,
            int iterations = Iterations)
        {
            _keySize = keySize;
            _saltSize = saltSize;
            _iterations = iterations;
        }

        public override byte[] Encrypt(
            ReadOnlySpan<byte> key,
            ReadOnlySpan<byte> plaintext)
        {
            var salt = Rand.SecureRandomBytes(_saltSize);

            using var derivedKeyBytes = new Rfc2898DeriveBytes(
                password: key.ToArray(),
                salt: salt,
                iterations: _iterations);

            using var aes = new AesManaged();
            aes.KeySize = _keySize;
            aes.Key     = derivedKeyBytes.GetBytes(aes.KeySize / 8);
            aes.IV      = derivedKeyBytes.GetBytes(aes.BlockSize / 8);
            aes.Mode    = CipherMode.CBC;

            using var ms = new MemoryStream();
            ms.Write(buffer: salt, offset: 0, count: salt.Length);

            using (var cs = new CryptoStream(
                stream: ms,
                transform: aes.CreateEncryptor(),
                mode: CryptoStreamMode.Write))
            {
                cs.Write(
                    buffer: plaintext.ToArray(),
                    offset: 0,
                    count: plaintext.Length);
            }

            return ms.ToArray();
        }

        public override void Encrypt(
            ReadOnlySpan<byte> key,
            ReadOnlySpan<byte> plaintext,
            Span<byte> ciphertext)
        {
            Encrypt(key, plaintext).AsSpan().CopyTo(ciphertext);
        }

        public override byte[] Decrypt(
            ReadOnlySpan<byte> key,
            ReadOnlySpan<byte> ciphertext)
        {
            var salt = ciphertext[.._saltSize].ToArray();

            using var derivedKeyBytes = new Rfc2898DeriveBytes(
                password: key.ToArray(),
                salt: salt,
                iterations: _iterations);

            using var aes = new AesManaged();
            aes.KeySize = _keySize;
            aes.Key     = derivedKeyBytes.GetBytes(cb: aes.KeySize / 8);
            aes.IV      = derivedKeyBytes.GetBytes(cb: aes.BlockSize / 8);
            aes.Mode    = CipherMode.CBC;

            using var ms = new MemoryStream();
            using (var cs = new CryptoStream(ms, aes.CreateDecryptor(), CryptoStreamMode.Write))
            {
                cs.Write(
                    buffer: ciphertext.ToArray(),
                    offset: _saltSize,
                    count: ciphertext.Length - _saltSize);
            }

            return ms.ToArray();
        }

        public override void Decrypt(
            ReadOnlySpan<byte> key,
            ReadOnlySpan<byte> ciphertext,
            Span<byte> plaintext)
        {
            Decrypt(key, ciphertext).AsSpan().CopyTo(plaintext);
        }
    }
}