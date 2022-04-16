using System;

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
        private bool disposedValue;

        public TezosKey(SecureBytes seed)
        {
            BcEd25519.GenerateKeyPair(
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

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    _privateKey?.Dispose();
                    _publicKey?.Dispose();
                }

                disposedValue = true;
            }
        }

        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}