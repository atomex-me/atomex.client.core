using Atomex.Common.Memory;

namespace Atomex.Wallets.Ethereum
{
    public class EthereumWallet : Wallet<EthereumKey>
    {
        public EthereumWallet(SecureBytes privateKey)
            : base(privateKey)
        {
        }
    }
}