using System;

using NBitcoin;

using Atomex.Common;
using Atomex.Common.Memory;
using Atomex.Cryptography;

namespace Atomex.Wallet.BitcoinBased
{
    public class BitcoinBasedKey : IKey
    {
        private Key Key { get; }

        public BitcoinBasedKey(SecureBytes seed)
        {
            var scopedSeed = seed.ToUnsecuredBytes();

            Key = new Key(scopedSeed);
        }

        public SecureBytes GetPrivateKey()
        {
            var privateKey = Key.ToBytes();

            return new SecureBytes(privateKey);
        }

        public SecureBytes GetPublicKey()
        {
            var publicKey = Key.PubKey.ToBytes();

            return new SecureBytes(publicKey);
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

        public void Dispose()
        {
            //Key.Dispose(); // may be nbitcoin learn to dispose keys?
        }
    }
}