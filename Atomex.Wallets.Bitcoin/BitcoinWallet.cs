using Atomex.Common.Memory;

namespace Atomex.Wallets.Bitcoin
{
    public class BitcoinWallet : Wallet<BitcoinKey>
    {
        public BitcoinWallet(SecureBytes privateKey)
            : base(privateKey, signDataType: SignDataType.Hash)
        {
        }
    }
}