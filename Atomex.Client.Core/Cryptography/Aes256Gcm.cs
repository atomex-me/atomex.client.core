using System;

using Atomex.Common.Libsodium;
using Atomex.Cryptography.Abstract;

namespace Atomex.Cryptography
{
    public class Aes256Gcm : AeadAlgorithm
    {
        private readonly AeadAlgorithm _impl;

        public Aes256Gcm()
        {
            _impl = Sodium.IsInitialized
                ? (AeadAlgorithm)new Libsodium.Aes256Gcm()
                : new BouncyCastle.Aes256Gcm();
        }

        public override byte[] Encrypt(
            ReadOnlySpan<byte> key,
            ReadOnlySpan<byte> nonce,
            ReadOnlySpan<byte> associatedData,
            ReadOnlySpan<byte> plaintext) =>
            _impl.Encrypt(key, nonce, associatedData, plaintext);

        public override void Encrypt(
            ReadOnlySpan<byte> key,
            ReadOnlySpan<byte> nonce,
            ReadOnlySpan<byte> associatedData,
            ReadOnlySpan<byte> plaintext,
            Span<byte> ciphertext) =>
            _impl.Encrypt(key, nonce, associatedData, plaintext, ciphertext);

        public override bool Decrypt(
            ReadOnlySpan<byte> key,
            ReadOnlySpan<byte> nonce,
            ReadOnlySpan<byte> associatedData,
            ReadOnlySpan<byte> ciphertext,
            Span<byte> plaintext) =>
            _impl.Decrypt(key, nonce, associatedData, ciphertext, plaintext);
    }
}