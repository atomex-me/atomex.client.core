using Atomex.Common.Memory;
using Atomex.Core;

namespace Atomex.Wallet.Abstract
{
    public interface IKeyStorage
    {
        SecureBytes GetPrivateKey(
            CurrencyConfig currency,
            string keyPath,
            int keyType);

        SecureBytes GetPublicKey(
            CurrencyConfig currency,
            string keyPath,
            int keyType);
    }
}