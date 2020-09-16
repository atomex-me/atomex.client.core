using System;
using System.Diagnostics;

using Atomex.Cryptography.Abstract;
using static Atomex.Common.Libsodium.Interop.Libsodium;

namespace Atomex.Cryptography.Libsodium
{
    public class Aes256Gcm : AeadAlgorithm
    {
        public override byte[] Encrypt(
            ReadOnlySpan<byte> key,
            ReadOnlySpan<byte> nonce,
            ReadOnlySpan<byte> associatedData,
            ReadOnlySpan<byte> plaintext)
        {
            var ciphertext = new byte[plaintext.Length + crypto_aead_aes256gcm_ABYTES];

            Encrypt(key, nonce, associatedData, plaintext, ciphertext);

            return ciphertext;
        }

        public unsafe override void Encrypt(
            ReadOnlySpan<byte> key,
            ReadOnlySpan<byte> nonce,
            ReadOnlySpan<byte> associatedData,
            ReadOnlySpan<byte> plaintext,
            Span<byte> ciphertext)
        {
            Debug.Assert(key.Length == crypto_aead_aes256gcm_KEYBYTES);
            Debug.Assert(nonce.Length == crypto_aead_aes256gcm_NPUBBYTES);
            Debug.Assert(ciphertext.Length == plaintext.Length + crypto_aead_aes256gcm_ABYTES);

            fixed (byte* c = ciphertext)
            fixed (byte* m = plaintext)
            fixed (byte* ad = associatedData)
            fixed (byte* n = nonce)
            fixed (byte* k = key)
            {
                int error = crypto_aead_aes256gcm_encrypt(
                    c,
                    out ulong clen_p,
                    m,
                    (ulong)plaintext.Length,
                    ad,
                    (ulong)associatedData.Length,
                    null,
                    n,
                    k);

                Debug.Assert(error == 0);
                Debug.Assert((ulong)ciphertext.Length == clen_p);
            }
        }

        public unsafe override bool Decrypt(
            ReadOnlySpan<byte> key,
            ReadOnlySpan<byte> nonce,
            ReadOnlySpan<byte> associatedData,
            ReadOnlySpan<byte> ciphertext,
            Span<byte> plaintext)
        {
            Debug.Assert(key.Length == crypto_aead_aes256gcm_KEYBYTES);
            Debug.Assert(nonce.Length == crypto_aead_aes256gcm_NPUBBYTES);
            Debug.Assert(plaintext.Length == ciphertext.Length - crypto_aead_aes256gcm_ABYTES);

            fixed (byte* m = plaintext)
            fixed (byte* c = ciphertext)
            fixed (byte* ad = associatedData)
            fixed (byte* n = nonce)
            fixed (byte* k = key)
            {
                int error = crypto_aead_aes256gcm_decrypt(
                    m,
                    out ulong mlen_p,
                    null,
                    c,
                    (ulong)ciphertext.Length,
                    ad,
                    (ulong)associatedData.Length,
                    n,
                    k);

                Debug.Assert(error != 0 || (ulong)plaintext.Length == mlen_p);
                return error == 0;
            }
        }
    }
}