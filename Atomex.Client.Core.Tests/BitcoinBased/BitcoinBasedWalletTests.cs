using Atomex.Common.Memory;
using Atomex.Client.Core.Tests.Wallets;
using Atomex.Cryptography;
using Atomex.Wallets.Abstract;
using Atomex.Wallets.BitcoinBased;

namespace Atomex.Client.Core.Tests.BitcoinBased
{
    public class BitcoinBasedWalletTests : WalletTests<BitcoinBasedWallet>
    {
        public override IWallet CreateWallet()
        {
            using var seed = new SecureBytes(Rand.SecureRandomBytes(32));

            return new BitcoinBasedWallet(seed);
        }
    }
}