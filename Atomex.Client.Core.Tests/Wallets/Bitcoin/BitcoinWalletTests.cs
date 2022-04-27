using Atomex.Common.Memory;
using Atomex.Cryptography;
using Atomex.Wallets.Abstract;

namespace Atomex.Wallets.Bitcoin
{
    public class BitcoinWalletTests : WalletTests<BitcoinWallet>
    {
        public override IWallet CreateWallet()
        {
            using var seed = new SecureBytes(Rand.SecureRandomBytes(32));

            return new BitcoinWallet(seed);
        }
    }
}