using Atomex.Common.Memory;
using Atomex.Cryptography;
using Atomex.Wallets.Abstract;
using Atomex.Wallets.Ethereum;

using Atomex.Client.Core.Tests.Wallets;

namespace Atomex.Client.Core.Tests.Ethereum
{
    public class EthereumWalletTests : WalletTests<EthereumWallet>
    {
        public override IWallet CreateWallet()
        {
            using var seed = new SecureBytes(Rand.SecureRandomBytes(32));

            return new EthereumWallet(seed);
        }
    }
}