using System;
using System.Threading;

namespace Atomex.Cryptography.Abstract
{
    public abstract class AeadAlgorithm
    {
        private static Aes256Gcm _aes256Gcm;
        public static Aes256Gcm Aes256Gcm
        {
            get {
                var instance = _aes256Gcm;

                if (instance == null)
                {
                    Interlocked.CompareExchange(ref _aes256Gcm, new Aes256Gcm(), null);
                    instance = _aes256Gcm;
                }

                return instance;
            }
        }

        public abstract byte[] Encrypt(
            ReadOnlySpan<byte> key,
            ReadOnlySpan<byte> nonce,
            ReadOnlySpan<byte> associatedData,
            ReadOnlySpan<byte> plaintext);

        public abstract void Encrypt(
            ReadOnlySpan<byte> key,
            ReadOnlySpan<byte> nonce,
            ReadOnlySpan<byte> associatedData,
            ReadOnlySpan<byte> plaintext,
            Span<byte> ciphertext);

        public abstract bool Decrypt(
            ReadOnlySpan<byte> key,
            ReadOnlySpan<byte> nonce,
            ReadOnlySpan<byte> associatedData,
            ReadOnlySpan<byte> ciphertext,
            Span<byte> plaintext);
    }
}