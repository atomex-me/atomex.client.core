using Atomex.Common.Memory;

namespace Atomex.Wallets.Abstract
{
    public abstract class CurrencyConfig
    {
        public string Name { get; }
        public decimal DecimalsMultiplier { get; }

        public abstract string AddressFromKey(
            SecureBytes publicKey,
            WalletInfo walletInfo = null);
    }
}