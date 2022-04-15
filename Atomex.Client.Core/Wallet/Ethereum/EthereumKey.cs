using Nethereum.Signer;

using Atomex.Common.Memory;
using Atomex.Wallet.BitcoinBased;

namespace Atomex.Wallet.Ethereum
{
    public class EthereumKey : BitcoinBasedKey
    {
        public EthereumKey(SecureBytes seed)
            : base(seed)
        {
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
            var privateKey = securePrivateKey.ToUnsecuredBytes();

            return new EthECKey(privateKey, true);
        }
    }
}