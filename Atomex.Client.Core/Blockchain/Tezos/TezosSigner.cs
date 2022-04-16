using System;

using Atomex.Blockchain.Tezos.Internal;
using Atomex.Common;
using Atomex.Cryptography;
using Atomex.Cryptography.Abstract;
using Atomex.Cryptography.BouncyCastle;

namespace Atomex.Blockchain.Tezos
{
    public static class TezosSigner
    {
        public static byte[] Sign(
            byte[] data,
            byte[] privateKey)
        {
            return BcEd25519.Sign(
                privateKey: privateKey,
                data: data);
        }

        public static byte[] SignByExtendedKey(
            byte[] data,
            byte[] extendedPrivateKey)
        {
            return BcEd25519.SignWithExtendedKey(
                privateKey: extendedPrivateKey,
                data: data);
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

            var hash = MacAlgorithm.HmacBlake2b.Mac(key: null, dataForSign);

            var signature = signer(privateKey, hash);

            return new SignedMessage
            {
                Bytes            = dataForSign,
                SignedHash       = signature,
                EncodedSignature = Base58Check.Encode(signature, Prefix.Edsig),
                SignedBytes      = data.ToHexString() + signature.ToHexString()
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
                    (pkKey, data) => BcEd25519.SignWithExtendedKey(pkKey, data),
                    watermark);
 
            return SignHash(data,
                privateKey,
                (pkKey, data) => BcEd25519.Sign(pkKey, data),
                watermark);
        }

        public static bool Verify(
            byte[] data,
            byte[] signature,
            byte[] publicKey)
        {
            return BcEd25519.Verify(
                publicKey: publicKey,
                data: data,
                signature: signature);
        }

        public static bool VerifyHash(
            byte[] data,
            byte[] signature,
            byte[] publicKey)
        {
            var hash = MacAlgorithm.HmacBlake2b.Mac(key: null, data);

            return BcEd25519.Verify(
                publicKey: publicKey,
                data: hash,
                signature: signature);
        }
    }
}