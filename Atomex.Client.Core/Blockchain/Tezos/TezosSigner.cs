using System;
using Atomex.Blockchain.Tezos.Internal;
using Atomex.Common;
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
            return Ed25519.Sign(data, privateKey);
        }

        public static byte[] SignByExtendedKey(
            byte[] data,
            byte[] extendedPrivateKey)
        {
            return Ed25519.SignByExtendedKey(data, extendedPrivateKey);
        }

        private static SignedMessage SignHash(
            byte[] data,
            byte[] privateKey,
            Func<byte[], byte[], byte[]> signer,
            byte[] watermark = null)
        {
            var dataForSign = data.Copy(offset: 0, count: data.Length);

            if (watermark?.Length > 0)
            {
                var bytesWithWatermark = new byte[dataForSign.Length + watermark.Length];

                Array.Copy(
                    sourceArray: watermark,
                    sourceIndex: 0,
                    destinationArray: bytesWithWatermark,
                    destinationIndex: 0,
                    length: watermark.Length);

                Array.Copy(
                    sourceArray: dataForSign,
                    sourceIndex: 0,
                    destinationArray: bytesWithWatermark,
                    destinationIndex: watermark.Length,
                    length: dataForSign.Length);

                dataForSign = bytesWithWatermark;
            }

            var hash = new HmacBlake2b(SignedMessage.HashSizeBits)
                .ComputeHash(dataForSign);

            var signature = signer(hash, privateKey);

            return new SignedMessage
            {
                Bytes = dataForSign,
                SignedHash = signature,
                EncodedSignature = Base58Check.Encode(signature, Prefix.Edsig),
                SignedBytes = data.ToHexString() + signature.ToHexString()
            };
        }

        public static SignedMessage SignHash(
            byte[] data,
            byte[] privateKey,
            byte[] watermark = null,
            bool isExtendedKey = true)
        {
            if (isExtendedKey)
                return SignHash(data,
                    privateKey,
                    Ed25519.SignByExtendedKey,
                    watermark);
 
            return SignHash(data,
                privateKey,
                Ed25519.Sign,
                watermark);
        }

        public static bool Verify(
            byte[] data,
            byte[] signature,
            byte[] publicKey)
        {
            return Ed25519.Verify(data, signature, publicKey);
        }

        public static bool VerifyHash(
            byte[] data,
            byte[] signature,
            byte[] publicKey)
        {
            var hash = new HmacBlake2b(digestSize: SignedMessage.HashSizeBits)
                .ComputeHash(input: data);

            return Ed25519.Verify(
                data: hash,
                signature: signature,
                publicKey: publicKey);
        }
    }
}