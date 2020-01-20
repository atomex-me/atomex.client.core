using Atomex.Common;
using Atomex.Cryptography;
using Atomex.Wallet.BitcoinBased;
using NBitcoin;
using Nethereum.Signer;

namespace Atomex.Wallet.Ethereum
{
    public class EthereumExtKey : BitcoinBasedExtKey
    {
        public EthereumExtKey(SecureBytes seed)
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
            using var securePrivateKey = GetPrivateKey();
            using var privateKey = securePrivateKey.ToUnsecuredBytes();

            return new EthECKey(privateKey, true);
        }
    }

}