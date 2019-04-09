using System;
using BcEd25519 = Org.BouncyCastle.Math.EC.Rfc8032.Ed25519;

namespace Atomix.Cryptography
{
    public class Ed25519
    {
        public const int PublicKeySize = 32;
        public const int PrivateKeySize = 32;
        public const int ExtendedPrivateKeySize = 64;

        public byte[] Sign(byte[] data, byte[] privateKey)
        {
            var signature = new byte[BcEd25519.SignatureSize];

            BcEd25519.Sign(privateKey, 0, data, 0, data.Length, signature, 0);

            return signature;
        }

        public bool Verify(byte[] data, byte[] signature, byte[] publicKey)
        {
            return BcEd25519.Verify(signature, 0, publicKey, 0, data, 0, data.Length);
        }

        public void GenerateKeyPair(byte[] seed, out byte[] privateKey, out byte[] publicKey)
        {
            privateKey = new byte[ExtendedPrivateKeySize];
            publicKey = new byte[PublicKeySize];

            BcEd25519.GeneratePublicKey(seed, 0, publicKey, 0);

            Array.Copy(seed, 0, privateKey, 0, PrivateKeySize);
            Array.Copy(publicKey, 0, privateKey, PrivateKeySize, PublicKeySize);
        }
    }
}