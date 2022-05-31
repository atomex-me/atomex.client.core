using Atomex.Common.Memory;
using Atomex.Cryptography;
using Atomex.Wallets.Abstract;
using Atomex.Wallets.Keys;

namespace Atomex.Wallets.Tezos
{
    public class TezosWalletTests : WalletTests<TezosWallet<Ed25519Key>>
    {
        public override IWallet CreateWallet()
        {
            using var seed = new SecureBytes(Rand.SecureRandomBytes(32));

            return new TezosWallet<Ed25519Key>(seed);
        }
    }
}