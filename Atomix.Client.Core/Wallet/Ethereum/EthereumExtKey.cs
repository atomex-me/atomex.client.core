using Atomix.Common;
using Atomix.Cryptography;
using Atomix.Wallet.BitcoinBased;
using NBitcoin;
using Nethereum.Signer;

namespace Atomix.Wallet.Ethereum
{
    public class EthereumExtKey : BitcoinBasedExtKey
    {
        public EthereumExtKey(byte[] seed)
            : base(seed)
        {
        }

        private EthereumExtKey(ExtKey key)
            : base(key)
        {
        }

        public override IExtKey Derive(uint index)
        {
            return new EthereumExtKey(Key.Derive(index));
        }

        public override IExtKey Derive(KeyPath keyPath)
        {
            return new EthereumExtKey(Key.Derive(keyPath));
        }

        public override byte[] SignHash(byte[] hash)
        {
            return GetEcKey()
                .Sign(hash)
                .ToDER();
        }

        public override byte[] SignMessage(byte[] data)
        {
            return GetEcKey()
                .Sign(data)
                .ToDER();
        }

        public override bool VerifyHash(byte[] hash, byte[] signature)
        {
            return GetEcKey()
                .Verify(hash, EthECDSASignature.FromDER(signature));
        }

        public override bool VerifyMessage(byte[] data, byte[] signature)
        {
            return GetEcKey()
                .Verify(data, EthECDSASignature.FromDER(signature));
        }

        private EthECKey GetEcKey()
        {
            GetPrivateKey(out var privateKey);

            try
            {
                // todo: use SecureString instead
                return new EthECKey(privateKey.ToHexString());
            }
            finally
            {
                privateKey.Clear();
            }
        }
    }
}