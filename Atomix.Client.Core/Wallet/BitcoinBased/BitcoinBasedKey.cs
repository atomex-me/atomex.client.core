using System;
using Atomix.Cryptography;
using NBitcoin;

namespace Atomix.Wallet.BitcoinBased
{
    public class BitcoinBasedKey : IKey
    {
        private Key Key { get; }

        public BitcoinBasedKey(byte[] seed)
        {
            Key = new Key(seed);
        }

        public void GetPrivateKey(out byte[] privateKey)
        {
            privateKey = Key.ToBytes();
        }

        public void GetPublicKey(out byte[] publicKey)
        {
            publicKey = Key.PubKey.ToBytes();
        }

        public virtual byte[] SignHash(byte[] hash)
        {
            return Key
                .Sign(new uint256(hash), SigHash.All)
                .ToBytes();
        }

        public virtual byte[] SignMessage(byte[] data)
        {
            return Convert.FromBase64String(Key
                .SignMessage(data));
        }

        public virtual bool VerifyHash(byte[] hash, byte[] signature)
        {
            if (hash == null)
                throw new ArgumentNullException(nameof(hash));

            if (signature == null)
                throw new ArgumentNullException(nameof(signature));

            return Key
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
                .PubKey
                .VerifyMessage(data, Convert.ToBase64String(signature));
        }
    }
}