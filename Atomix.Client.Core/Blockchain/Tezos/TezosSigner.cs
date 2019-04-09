using System;
using Atomix.Blockchain.Tezos.Internal;
using Atomix.Common;
using Atomix.Cryptography;

namespace Atomix.Blockchain.Tezos
{
    public class TezosSigner
    {
        public byte[] Sign(byte[] data, byte[] privateKey)
        {
            return new Ed25519().Sign(data, privateKey);
        }

        public SignedMessage SignHash(byte[] data, byte[] privateKey, byte[] watermark = null)
        {
            var dataForSign = data.Copy(0, data.Length);

            if (watermark?.Length > 0)
            {
                var bytesWithWatermark = new byte[dataForSign.Length + watermark.Length];

                Array.Copy(watermark, 0, bytesWithWatermark, 0, watermark.Length);
                Array.Copy(dataForSign, 0, bytesWithWatermark, watermark.Length, dataForSign.Length);

                dataForSign = bytesWithWatermark;
            }

            var hash = new HmacBlake2b(SignedMessage.HashSizeBits)
                .ComputeHash(dataForSign);

            var signature = new Ed25519().Sign(hash, privateKey);

            return new SignedMessage
            {
                Bytes = dataForSign,
                SignedHash = signature,
                EncodedSignature = Base58Check.Encode(signature, Prefix.Edsig),
                SignedBytes = data.ToHexString() + signature.ToHexString()
            };
        }

        public bool Verify(byte[] data, byte[] signature, byte[] publicKey)
        {
            return new Ed25519().Verify(data, signature, publicKey);
        }

        public bool VerifyHash(byte[] data, byte[] signature, byte[] publicKey)
        {
            var hash = new HmacBlake2b(SignedMessage.HashSizeBits)
                .ComputeHash(data);

            return new Ed25519().Verify(hash, signature, publicKey);
        }
    }
}