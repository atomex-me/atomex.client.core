using System;
using Atomex.Cryptography;
using NBitcoin;

namespace Atomex.Wallet.BitcoinBased
{
    public class BitcoinBasedExtKey : IExtKey
    {
        protected ExtKey Key { get; }

        public BitcoinBasedExtKey(byte[] seed)
        {
            Key = new ExtKey(seed);
        }

        protected BitcoinBasedExtKey(ExtKey key)
        {
            Key = key;
        }

        public virtual IExtKey Derive(uint index)
        {
            return new BitcoinBasedExtKey(Key.Derive(index));
        }

        public virtual IExtKey Derive(KeyPath keyPath)
        {
            return new BitcoinBasedExtKey(Key.Derive(keyPath));
        }

        public virtual void GetPrivateKey(out byte[] privateKey)
        {
            // todo: dot not store key in heap
            privateKey = Key.PrivateKey.ToBytes();
        }

        public virtual void GetPublicKey(out byte[] publicKey)
        {
            // todo: dot not store key in heap
            publicKey = Key.PrivateKey.PubKey.ToBytes();
        }

        public virtual byte[] SignHash(byte[] hash)
        {
            return Key
                .PrivateKey
                .Sign(new uint256(hash), SigHash.All)
                .ToBytes();
        }

        public virtual byte[] SignMessage(byte[] data)
        {
            return Convert.FromBase64String(Key
                .PrivateKey
                .SignMessage(data));
        }

        public virtual bool VerifyHash(byte[] hash, byte[] signature)
        {
            if (hash == null)
                throw new ArgumentNullException(nameof(hash));

            if (signature == null)
                throw new ArgumentNullException(nameof(signature));

            return Key
                .PrivateKey
                .PubKey
                .Verify(new uint256(hash), new TransactionSignature(signature).Signature);
        }

        public virtual bool VerifyMessage(byte[] data, byte[] signature)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));

            if (signature == null)
                throw new ArgumentNullException(nameof(signature));

            return Key
                .PrivateKey
                .PubKey
                .VerifyMessage(data, Convert.ToBase64String(signature));
        }
    }
}