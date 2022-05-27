using Atomex.Common.Memory;

namespace Atomex.Wallets.Abstract
{
    public abstract class CurrencyConfig
    {
        public string Name { get; }
        public int Decimals { get; }

        public abstract string AddressFromKey(
            SecureBytes publicKey,
            WalletInfo walletInfo = null);
    }
}