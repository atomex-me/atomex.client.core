using Atomex.Common.Memory;
using Atomex.Cryptography;
using Atomex.Wallets.Abstract;
using Atomex.Wallets.Tezos;

using Atomex.Client.Core.Tests.Wallets;

namespace Atomex.Client.Core.Tests.Tezos
{
    public class TezosWalletTests : WalletTests<TezosWallet>
    {
        public override IWallet CreateWallet()
        {
            using var seed = new SecureBytes(Rand.SecureRandomBytes(32));

            return new TezosWallet(seed);
        }
    }
}