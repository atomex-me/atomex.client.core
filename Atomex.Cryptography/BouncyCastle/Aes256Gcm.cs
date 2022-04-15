using System;

using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Modes;
using Org.BouncyCastle.Crypto.Parameters;

using Atomex.Cryptography.Abstract;

namespace Atomex.Cryptography.BouncyCastle
{
    public class Aes256Gcm : AeadAlgorithm
    {
        private const int MacSize = 16 * 8;

        public static byte[] Encrypt(
            byte[] key,
            byte[] nonce,
            byte[] associatedData,
            byte[] plaintext)
        {
            var cipher = new GcmBlockCipher(new AesEngine());

            var keyParameters = new KeyParameter(key);

            var parameters = new AeadParameters(
                key: keyParameters,
                macSize: MacSize,
                nonce: nonce,
                associatedText: associatedData);

            cipher.Init(forEncryption: true, parameters);

            var encryptedBytes = new byte[cipher.GetOutputSize(plaintext.Length)];

            var processed = cipher.ProcessBytes(
                input: plaintext,
                inOff: 0,
                len: plaintext.Length,
                output: encryptedBytes,
                outOff: 0);

            cipher.DoFinal(encryptedBytes, processed);

            return encryptedBytes;
        }

        public override byte[] Encrypt(
            ReadOnlySpan<byte> key,
            ReadOnlySpan<byte> nonce,
            ReadOnlySpan<byte> associatedData,
            ReadOnlySpan<byte> plaintext)
        {
            return Encrypt(
                key.ToArray(),
                nonce.ToArray(),
                associatedData.ToArray(),
                plaintext.ToArray());
        }

        public override void Encrypt(
            ReadOnlySpan<byte> key,
            ReadOnlySpan<byte> nonce,
            ReadOnlySpan<byte> associatedData,
            ReadOnlySpan<byte> plaintext,
            Span<byte> ciphertext)
        {
            Encrypt(
                key.ToArray(),
                nonce.ToArray(),
                associatedData.ToArray(),
                plaintext.ToArray()).CopyTo(ciphertext);
        }

        public static byte[] Decrypt(
            byte[] key,
            byte[] nonce,
            byte[] associatedData,
            byte[] ciphertext)
        {
            var cipher = new GcmBlockCipher(new AesEngine());

            var keyParameters = new KeyParameter(key);

            var parameters = new AeadParameters(
                key: keyParameters,
                macSize: MacSize,
                nonce: nonce,
                associatedText: associatedData);

            cipher.Init(forEncryption: false, parameters);

            var plainBytes = new byte[cipher.GetOutputSize(ciphertext.Length)];

            var processed = cipher.ProcessBytes(
                input: ciphertext,
                inOff: 0,
                len: ciphertext.Length,
                output: plainBytes,
                outOff: 0);

            cipher.DoFinal(plainBytes, processed);

            return plainBytes;
        }

        public override bool Decrypt(
            ReadOnlySpan<byte> key,
            ReadOnlySpan<byte> nonce,
            ReadOnlySpan<byte> associatedData,
            ReadOnlySpan<byte> ciphertext,
            Span<byte> plaintext)
        {
            Decrypt(
                key.ToArray(),
                nonce.ToArray(),
                associatedData.ToArray(),
                ciphertext.ToArray()).CopyTo(plaintext);

            return true;
        }
    }
}