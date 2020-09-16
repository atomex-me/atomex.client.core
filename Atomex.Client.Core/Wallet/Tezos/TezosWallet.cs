using Atomex.Common.Memory;

namespace Atomex.Wallets.Tezos
{
    public class TezosWallet : Wallet<TezosKey>
    {
        public TezosWallet(SecureBytes privateKey)
            : base(privateKey)
        {
        }
    }
}