using Atomex.Blockchain.Tezos;
using Atomex.Common.Memory;
using Atomex.Cryptography;
using Atomex.Cryptography.BouncyCastle;

namespace Atomex.Wallet.Tezos
{
    public class TezosKey : IKey
    {
        private readonly SecureBytes _privateKey;
        private readonly SecureBytes _publicKey;

        public TezosKey(SecureBytes seed)
        {
            Ed25519.GenerateKeyPair(
                seed: seed,
                privateKey: out _privateKey,
                publicKey: out _publicKey);
        }

        public SecureBytes GetPrivateKey()
        {
            return _privateKey.Copy();
        }

        public SecureBytes GetPublicKey()
        {
            return _publicKey.Copy();
        }

        public byte[] SignHash(byte[] hash)
        {
            using var securePrivateKey = GetPrivateKey();
            var scopedPrivateKey = securePrivateKey.ToUnsecuredBytes();

            if (scopedPrivateKey.Length == 32)
                return TezosSigner.Sign(
                    data: hash,
                    privateKey: scopedPrivateKey);

            return TezosSigner.SignByExtendedKey(
                data: hash,
                extendedPrivateKey: scopedPrivateKey);
        }

        public byte[] SignMessage(byte[] data)
        {
            using var securePrivateKey = GetPrivateKey();
            var scopedPrivateKey = securePrivateKey.ToUnsecuredBytes();

            if (scopedPrivateKey.Length == 32)
                return TezosSigner.Sign(
                    data: data,
                    privateKey: scopedPrivateKey);

            return TezosSigner.SignByExtendedKey(
                data: data,
                extendedPrivateKey: scopedPrivateKey);
        }

        public bool VerifyHash(byte[] hash, byte[] signature)
        {
            using var securePublicKey = GetPublicKey();
            var scopedPublicKey = securePublicKey.ToUnsecuredBytes();

            return TezosSigner.Verify(
                data: hash,
                signature: signature,
                publicKey: scopedPublicKey);
        }

        public bool VerifyMessage(byte[] data, byte[] signature)
        {
            using var securePublicKey = GetPublicKey();
            var scopedPublicKey = securePublicKey.ToUnsecuredBytes();

            return TezosSigner.Verify(
                data: data,
                signature: signature,
                publicKey: scopedPublicKey);
        }

        public void Dispose()
        {
            if (_privateKey != null)
                _privateKey.Dispose();

            if (_publicKey != null)
                _publicKey.Dispose();
        }
    }
}