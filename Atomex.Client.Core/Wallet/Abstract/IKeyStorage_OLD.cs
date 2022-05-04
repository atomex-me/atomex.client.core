using Atomex.Common.Memory;
using Atomex.Core;

namespace Atomex.Wallet.Abstract
{
    public interface IKeyStorage_OLD
    {
        SecureBytes GetPrivateKey(
            CurrencyConfig_OLD currency,
            KeyIndex keyIndex,
            int keyType);

        SecureBytes GetPublicKey(
            CurrencyConfig_OLD currency,
            KeyIndex keyIndex,
            int keyType);
    }
}