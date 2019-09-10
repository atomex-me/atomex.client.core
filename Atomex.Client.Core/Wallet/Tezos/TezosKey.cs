using System;
using Atomex.Blockchain.Tezos;
using Atomex.Blockchain.Tezos.Internal;
using Atomex.Common;
using Atomex.Cryptography;
using Atomex.Cryptography.BouncyCastle;

namespace Atomex.Wallet.Tezos
{
    public class TezosKey : IKey
    {
        private Keys Keys { get; }

        public TezosKey(byte[] seed)
        {
            byte[] publicKey = null, privateKey = null;

            try
            {
                Ed25519.GenerateKeyPair(
                    seed: seed,
                    privateKey: out privateKey,
                    publicKey: out publicKey);

                Keys = new Keys(sk: privateKey, pk: publicKey);
            }
            finally
            {
                if (privateKey != null)
                    Array.Clear(privateKey, 0, privateKey.Length);

                if (publicKey != null)
                    Array.Clear(publicKey, 0, publicKey.Length);
            }
        }

        public void GetPrivateKey(out byte[] privateKey)
        {
            // todo: dot not store key in heap
            privateKey = Base58Check.Decode(Keys.DecryptPrivateKey(), Prefix.Edsk);
        }

        public void GetPublicKey(out byte[] publicKey)
        {
            // todo: dot not store key in heap
            publicKey = Base58Check.Decode(Keys.DecryptPublicKey(), Prefix.Edpk);
        }

        public byte[] SignHash(byte[] hash)
        {
            GetPrivateKey(out var privateKey);

            try
            {
                if (privateKey.Length == 32)
                    return TezosSigner.Sign(
                        data: hash,
                        privateKey: privateKey);

                return TezosSigner.SignByExtendedKey(
                        data: hash,
                        extendedPrivateKey: privateKey);
            }
            finally
            {
                privateKey.Clear();
            }
        }

        public byte[] SignMessage(byte[] data)
        {
            GetPrivateKey(out var privateKey);

            try
            {
                if (privateKey.Length == 32)
                    return TezosSigner.Sign(
                        data: data,
                        privateKey: privateKey);

                return TezosSigner.SignByExtendedKey(
                    data: data,
                    extendedPrivateKey: privateKey);
            }
            finally
            {
                privateKey.Clear();
            }
        }

        public bool VerifyHash(byte[] hash, byte[] signature)
        {
            GetPublicKey(out var publicKey);

            try
            {
                return TezosSigner.Verify(
                    data: hash,
                    signature: signature,
                    publicKey: publicKey);
            }
            finally
            {
                publicKey.Clear();
            }
        }

        public bool VerifyMessage(byte[] data, byte[] signature)
        {
            GetPublicKey(out var publicKey);

            try
            {
                return TezosSigner.Verify(
                    data: data,
                    signature: signature,
                    publicKey: publicKey);
            }
            finally
            {
                publicKey.Clear();
            }
        }
    }
}