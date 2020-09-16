using Atomex.Common.Memory;

namespace Atomex.Wallets.BitcoinBased
{
    public class BitcoinBasedWallet : Wallet<BitcoinBasedKey>
    {
        public BitcoinBasedWallet(SecureBytes privateKey)
            : base(privateKey)
        {
        }
    }
}