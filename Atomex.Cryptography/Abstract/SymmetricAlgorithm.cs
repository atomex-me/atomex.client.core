using System;
using System.Threading;

using Atomex.Cryptography.DotNet;

namespace Atomex.Cryptography.Abstract
{
    public abstract class SymmetricAlgorithm
    {
        private static AesCbc _aesCbc;
        public static AesCbc AesCbc
        {
            get
            {
                var instance = _aesCbc;

                if (instance == null)
                {
                    Interlocked.CompareExchange(ref _aesCbc, new AesCbc(), null);
                    instance = _aesCbc;
                }

                return instance;
            }
        }

        public abstract byte[] Encrypt(
            ReadOnlySpan<byte> key,
            ReadOnlySpan<byte> plaintext);

        public abstract void Encrypt(
            ReadOnlySpan<byte> key,
            ReadOnlySpan<byte> plaintext,
            Span<byte> ciphertext);

        public abstract byte[] Decrypt(
            ReadOnlySpan<byte> key,
            ReadOnlySpan<byte> ciphertext);

        public abstract void Decrypt(
            ReadOnlySpan<byte> key,
            ReadOnlySpan<byte> ciphertext,
            Span<byte> plaintext);
    }
}