using System;

using Atomex.Blockchain.Tezos.Internal;
using Atomex.Common;
using Atomex.Common.Memory;
using Atomex.Cryptography;
using Atomex.Cryptography.BouncyCastle;

namespace Atomex.Blockchain.Tezos
{
    public static class TezosSigner
    {
        public static byte[] Sign(
            byte[] data,
            byte[] privateKey)
        {
            return BcEd25519.Sign(data, privateKey);
        }

        public static byte[] SignByExtendedKey(
            byte[] data,
            byte[] extendedPrivateKey)
        {
            return BcEd25519.SignWithExtendedKey(data, extendedPrivateKey);
        }

        public static SignedMessage SignHash(
            ReadOnlySpan<byte> data,
            ReadOnlySpan<byte> privateKey,
            byte[] watermark = null,
            bool isExtendedKey = true)
        {
            using var dataForSign = new UnmanagedBytes(watermark?.Length > 0
                ? data.Length + watermark.Length
                : data.Length);

            if (watermark?.Length > 0)
            {
                watermark.CopyTo(dataForSign.GetSpan().Slice(0, watermark.Length));
                data.CopyTo(dataForSign.GetSpan().Slice(watermark.Length, data.Length));
            }
            else
            {
                data.CopyTo(dataForSign.GetSpan().Slice(0, data.Length));
            }

            var hash = HmacBlake2b.Compute(dataForSign.ToBytes(), SignedMessage.HashSizeBits);

            var signature = isExtendedKey
                ? BcEd25519.SignWithExtendedKey(privateKey, data)
                : BcEd25519.Sign(privateKey, data);

            return new SignedMessage
            {
                Bytes = dataForSign.ToBytes(),
                SignedHash = signature,
                EncodedSignature = Base58Check.Encode(signature, Prefix.Edsig),
                SignedBytes = data.ToArray().ToHexString() + signature.ToHexString()
            };
        }

        public static bool Verify(
            byte[] data,
            byte[] signature,
            byte[] publicKey)
        {
            return BcEd25519.Verify(data, signature, publicKey);
        }

        public static bool VerifyHash(
            byte[] data,
            byte[] signature,
            byte[] publicKey)
        {
            var hash = HmacBlake2b.Compute(data, SignedMessage.HashSizeBits);

            return BcEd25519.Verify(
                data: hash,
                signature: signature,
                publicKey: publicKey);
        }
    }
}